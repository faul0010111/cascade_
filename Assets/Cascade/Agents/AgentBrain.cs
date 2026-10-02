using System.Collections.Generic;
using Cascade.Core;
using Cascade.World;
using UnityEngine;

namespace Cascade.Agents
{
    public enum LodTier { Full = 0, Reduced = 1, Minimal = 2 }

    /// <summary>
    /// Composition root of one agent. Pure C#: owns the subsystems and runs the tick pipeline
    /// (ARCHITECTURE.md section 6). Has no Unity dependency beyond math types, so it runs headless in tests.
    /// </summary>
    public sealed class AgentBrain
    {
        public EntityId Id { get; }
        public string Name { get; }
        public AgentProfile Profile { get; }
        public bool IsPlayerControlled { get; }

        public Personality Personality { get; }
        public InternalState State { get; }
        public EmotionModel Emotion { get; } = new EmotionModel();
        public NeedSystem Needs { get; } = new NeedSystem();
        public BeliefStore Beliefs { get; } = new BeliefStore();
        public MemorySystem Memory { get; } = new MemorySystem();
        public RelationshipBook Relationships { get; }
        public PerceptionSystem Perception { get; }
        public Communicator Comms { get; }
        public RouteEvaluator Routes { get; }
        public GoalSelector Goals { get; }
        public AgentPlanner Planner { get; }
        public ActionExecutor Executor { get; } = new ActionExecutor();
        public DecisionLog Log { get; } = new DecisionLog();
        public CognitiveHistory History { get; } = new CognitiveHistory();
        public LearningSystem Learning { get; }
        public SelfAssessment Self { get; } = new SelfAssessment();
        public Rng Rng { get; }

        public IAgentBody Body { get; }
        public AgentServices Services { get; }
        public IFloorPlan FloorPlan => Services.FloorPlan;
        public GroupRegistry Groups => Services.Groups;
        public IAgentWorldActions World => Services.WorldActions;
        public CognitionSettings Cognition => Services.Cognition;

        public float Now { get; private set; }
        public LodTier Lod { get; private set; }
        public int CurrentRoom { get; private set; } = -1;
        public bool AtSafety => CurrentRoom >= 0 && FloorPlan.IsExterior(CurrentRoom);
        public bool IsInjured => State[StateVar.Health] < 0.4f;
        public bool Incapacitated => State[StateVar.Health] <= 0.001f;
        public EntityId LongTermTarget { get; set; }
        public int HeldExtinguisher { get; set; } = -1;
        public readonly List<int> CurrentRoute = new List<int>();

        public bool InterruptPending { get; private set; }
        public string InterruptReason { get; private set; }

        private float _nextDecision;
        private GoalOption _lastLoggedGoal = GoalOption.Of(GoalType.Evacuate);
        private float _sinceSense = 999f;
        private bool _injuredAnnounced;
        private bool _collapsedAnnounced;
        private float _lastSafetyArrival = -999f;
        private bool _doubtedPlan;
        private readonly Dictionary<EntityId, float> _answers = new Dictionary<EntityId, float>();
        private float _nextScream;
        private readonly List<EntityId> _scratch = new List<EntityId>();
        private readonly List<Belief> _beliefScratch = new List<Belief>();

        public AgentBrain(EntityId id, AgentProfile profile, AgentServices services, IAgentBody body, bool playerControlled = false,
                          List<GoalDefinition> goals = null, List<AgentAction> actions = null)
        {
            Id = id;
            Profile = profile;
            Name = profile.Name;
            Services = services;
            Body = body;
            IsPlayerControlled = playerControlled;
            Personality = profile.Personality;
            State = new InternalState(Personality);
            State.SetValue(StateVar.Health, profile.InitialHealth);
            Relationships = new RelationshipBook(Personality);
            Learning = new LearningSystem(this);
            Relationships.TrustBias = () => Learning.Dispositions.TrustBias;
            Rng = services.Sim.CreateRng("agent:" + id.Value);
            Perception = new PerceptionSystem(this, services.Perception);
            Comms = new Communicator(this);
            Routes = new RouteEvaluator(this);
            Goals = new GoalSelector(this, goals ?? GoalLibrary.CreateDefault());
            Planner = new AgentPlanner(actions ?? ActionLibrary.CreateDefault());
            _nextDecision = Rng.Range(0f, 0.5f); // desynchronize agents
        }

        public string NameOf(EntityId e) => e == Id ? "me" : Services.Sim.Entities.NameOf(e);

        // ---- Tick pipeline -----------------------------------------------------------------------------------------

        public void Tick(float now, float dt, LodTier lod)
        {
            Now = now;
            Lod = lod;

            SenseIfDue(now, dt, lod);
            Perception.ProcessQueued(now);
            Comms.ProcessInbox(now);
            if (IsPlayerControlled) return;

            if (Incapacitated)
            {
                Body.Stop();
                Body.SetPosture(Posture.Down);
                if (now >= _nextScream)
                {
                    _nextScream = now + 6f;
                    World.PublishSound(new SoundEvent { Kind = SoundKind.Scream, Position = Body.Position, Loudness = 14f, Source = Id });
                }
                return;
            }

            ApplyDispositions();
            State.Tick(dt);
            if (Emotion.Update(State, now, dt))
            {
                Log.Event(now, "Feels " + Emotion.Current.ToString().ToLowerInvariant());
                if (Emotion.Current == EmotionState.Panicked) RaiseInterrupt("panic");
            }
            if (now >= _nextDecision || InterruptPending || !Goals.HasGoal)
            {
                Self.Update(this, now);
                Needs.Update(this, now); // needs only feed decisions, so they are recomputed at decision time
                Decide(now);
            }
            HandleExecution(Executor.Tick(this, dt), now);
            CheckPlanConfidence(now);
            if (History.Due(now)) SampleHistory(now);
        }

        private void SenseIfDue(float now, float dt, LodTier lod)
        {
            _sinceSense += dt;
            float interval = lod == LodTier.Full ? 0.2f : lod == LodTier.Reduced ? 0.5f : 1f;
            if (_sinceSense < interval && CurrentRoom >= 0) return;
            _sinceSense = 0f;
            Perception.Sense(now);
        }

        private void Decide(float now)
        {
            string trigger = InterruptPending ? InterruptReason : (Goals.HasGoal ? "scheduled" : "no active goal");
            InterruptPending = false;
            InterruptReason = null;

            var trace = Goals.Decide(now, trigger);
            Log.Add(trace);
            float baseInterval = Lod == LodTier.Full ? 0.5f : Lod == LodTier.Reduced ? 1f : 2f;
            _nextDecision = now + baseInterval * Emotion.Modifiers.DecisionIntervalMul * Rng.Range(0.85f, 1.15f);

 if (trace.GoalChanged)
            {
                if (!Goals.Current.Equals(_lastLoggedGoal) || Goals.Current.Type != GoalType.Routine)
                    Log.Event(now, "New goal: " + Goals.Current.Label(this) + " (" + Mathf.RoundToInt(Mathf.Min(1f, Goals.CurrentScore) * 100) + "%)");
                _lastLoggedGoal = Goals.Current;
                Replan(now);
            }
            else if (!Executor.HasPlan) Replan(now);
        }

        public void Replan(float now)
        {
            Executor.Abort(this);
            CurrentRoute.Clear();
            bool complete;
            var plan = Planner.Plan(this, Goals.Current, out complete);
            if (plan.Count == 0)
            {
                if (complete) { Goals.OnAchieved(now); }
                else
                {
                    Goals.OnFailed(now, "NoPlan");
                    Log.Event(now, "Cannot find a way to " + Goals.Current.Label(this));
                }
                RaiseInterrupt(complete ? "goal already satisfied" : "no plan");
                return;
            }
            Executor.SetPlan(plan, complete);
            _doubtedPlan = false;
            var names = new string[plan.Count];
            for (int i = 0; i < plan.Count; i++) names[i] = plan[i].Name;
            if (Goals.Current.Type != GoalType.Routine)
                Log.Event(now, "Plan: " + string.Join(" > ", names) + (complete ? "" : " (partial)"));
        }

        private void HandleExecution(ExecResult result, float now)
        {
            switch (result)
            {
                case ExecResult.PlanSucceeded:
                    if (Goals.Current.Type != GoalType.Routine) Log.Event(now, "Achieved " + Goals.Current.Label(this));
                    Goals.OnAchieved(now);
                    RaiseInterrupt("goal achieved");
                    break;
                case ExecResult.PartialPlanEnded:
                    Replan(now);
                    break;
                case ExecResult.Failed:
                    Log.Event(now, Executor.LastFailedAction + " failed: " + Executor.FailureReason);
                    Appraise(AppraisalEvent.PlanFailed, 0.5f);
                    if (Goals.OnFailed(now, Executor.FailureReason)) RaiseInterrupt("goal keeps failing");
                    else Replan(now);
                    break;
            }
        }

        // ---- Hooks used by subsystems ------------------------------------------------------------------------------

        public void RaiseInterrupt(string reason)
        {
            if (!InterruptPending) InterruptReason = reason;
            InterruptPending = true;
        }

        public void Appraise(AppraisalEvent evt, float intensity) => Appraisal.Apply(evt, intensity, State, Personality);

        public void SetRoom(int room, float now)
        {
            if (room == CurrentRoom) return;
            bool wasSafe = AtSafety;
            int prevRoom = CurrentRoom;
            CurrentRoom = room;
            if (room < 0) return;
            Memory.RecordVisit(room);
            Routes.Invalidate();

            if (AtSafety && !wasSafe)
            {
                Appraise(AppraisalEvent.ReachedSafety, 1f);
                Memory.Record(MemoryKind.ReachedSafety, EntityId.None, room, now, 0.8f, 0.8f);
                Beliefs.Observe(FactKey.Safe(Id), 1f, 1f, now, BeliefSource.Perceived);
                Log.Event(now, "Reached safety (" + FloorPlan.RoomName(room) + ")");
                int exitDoor = DoorBetween(prevRoom, room);
                bool newArrival = now - _lastSafetyArrival > 20f; // stepping in and out of the doorway is not a new success
                _lastSafetyArrival = now;
                if (exitDoor >= 0 && newArrival) Learning.Outcome(StrategyKey.Exit(exitDoor), 1f, ExperienceSource.Direct, "reached safety");
                var direct = Groups.LeaderOf(Id);
                if (direct.IsValid && newArrival)
                {
                    Learning.Outcome(StrategyKey.Follow(direct), 1f, ExperienceSource.Direct, "reached safety", direct);
                    World.PublishSocialAction(new SocialActionEvent { Actor = direct, Target = Id, Kind = SocialActionKind.Led, Position = Body.Position });
                }
                var leader = Groups.RootLeader(Id);
                if (leader != Id)
                {
                    Relationships.Apply(leader, IsInjured ? SocialEventKind.SavedMyLife : SocialEventKind.LedMeToSafety, now);
                    Memory.Record(MemoryKind.LedBy, leader, room, now, 0.8f, 0.8f);
                }
                RaiseInterrupt("reached safety");
            }
            else if (!AtSafety && wasSafe)
            {
                Beliefs.Observe(FactKey.Safe(Id), 0f, 1f, now, BeliefSource.Perceived);
            }
        }

        public void HandleVerification(BeliefUpdate upd, float now)
        {
            if (upd.ContradictedTeller.IsValid)
            {
                var t = upd.ContradictedTeller;
                Relationships.Apply(t, SocialEventKind.GaveWrongInfo, now);
                UpdateReliability(t, 0f, now);
                Memory.Record(MemoryKind.InfoWasWrong, t, CurrentRoom, now, -0.5f, 0.6f);
                Appraise(AppraisalEvent.InfoProvedWrong, 1f);
                Emotion.AddConflict(0.35f);
                Log.Event(now, NameOf(t) + " was wrong about " + upd.Belief.Key);
            }
            else if (upd.ConfirmedTeller.IsValid)
            {
                Relationships.Apply(upd.ConfirmedTeller, SocialEventKind.GaveCorrectInfo, now);
                UpdateReliability(upd.ConfirmedTeller, 1f, now);
                Memory.Record(MemoryKind.InfoWasRight, upd.ConfirmedTeller, CurrentRoom, now, 0.3f, 0.4f);
            }
        }

        /// <summary>Physical harm is applied by the runtime from world truth; the agent experiences it as pain.</summary>
        public void ApplyDamage(float amount, float now)
        {
            if (amount <= 0f || Incapacitated) return;
            bool wasInjured = IsInjured;
            State.Add(StateVar.Health, -amount);
            if (wasInjured) _injuredAnnounced = true; // hurt before this moment (e.g. at scenario start): not caused by now
            if (IsInjured && !_injuredAnnounced)
            {
                _injuredAnnounced = true;
                Appraise(AppraisalEvent.Injured, 1f);
                Memory.Record(MemoryKind.RouteDangerous, EntityId.None, CurrentRoom, now, -1f, 1f);
                World.PublishSound(new SoundEvent { Kind = SoundKind.Scream, Position = Body.Position, Loudness = 16f, Source = Id });
                Log.Event(now, "I'm hurt!");
                RaiseInterrupt("injured");
                LearnFromHarm(now);
            }
            if (Incapacitated && !_collapsedAnnounced)
            {
                _collapsedAnnounced = true;
                Executor.Abort(this);
                DropExtinguisher();
                StopFollowing();
                Log.Event(now, "Collapsed");
            }
        }

        /// <summary>Whether the target appeared to be down the last time they were seen (a belief, not truth).</summary>
        public bool TargetLooksIncapacitated(EntityId target) => _downSeen.Contains(target) && Beliefs.Weighted(FactKey.Injured(target), Now) > 0.4f;

        private readonly HashSet<EntityId> _downSeen = new HashSet<EntityId>();
        public void NoteIncapacitated(EntityId target, bool down)
        {
            if (down) _downSeen.Add(target); else _downSeen.Remove(target);
        }

        public bool IsDoorOnCurrentRoute(int door) => CurrentRoute.Contains(door);

        /// <summary>Lab: forget everything experienced (beliefs, memories, learning, history). Personality and priors stay.</summary>
        public void ResetMind()
        {
            Executor.Abort(this);
            var priors = new List<Belief>();
            foreach (var bl in Beliefs.All) if (bl.Source.Kind == BeliefSourceKind.Prior) priors.Add(bl);
            var keys = new List<FactKey>();
            foreach (var bl in Beliefs.All) keys.Add(bl.Key);
            foreach (var k in keys) Beliefs.Forget(k);
            foreach (var p in priors) AddPrior(p.Key, p.Value, p.Position);
            Memory.Clear();
            Learning.Reset();
            History.Clear();
            Goals.ForceReevaluate();
            RaiseInterrupt("mind reset");
        }

        // ---- Cognition ---------------------------------------------------------------------------------------------

        private int DoorBetween(int a, int b)
        {
            if (a < 0 || b < 0) return -1;
            var doors = FloorPlan.DoorsOf(a);
            for (int i = 0; i < doors.Count; i++) if (FloorPlan.DoorOtherSide(doors[i], a) == b) return doors[i];
            return -1;
        }

        /// <summary>Getting hurt is an outcome: of the strategy being tried and, if following someone, of trusting them.</summary>
        private void LearnFromHarm(float now)
        {
            var active = Executor.ActiveStrategy;
            if (active.HasValue && active.Value.Kind != StrategyKind.FollowPerson)
            {
                Learning.Outcome(active.Value, 0.1f, ExperienceSource.Direct, "got hurt");
                Learning.Expect(active.Value);
            }
            var leader = Groups.LeaderOf(Id);
            if (!leader.IsValid) return;
            var rel = Relationships.Get(leader);
            float trustBefore = rel.Trust;
            Learning.Outcome(StrategyKey.Follow(leader), 0f, ExperienceSource.Direct, "led into danger", leader);
            World.PublishSocialAction(new SocialActionEvent { Actor = leader, Target = Id, Kind = SocialActionKind.Endangered, Position = Body.Position });
            // Betrayal hurts more the more you trusted them.
            Relationships.Apply(leader, SocialEventKind.LedMeIntoDanger, now);
            Memory.Record(MemoryKind.LedIntoDanger, leader, CurrentRoom, now, -0.9f, 0.9f);
            Learning.Event(LearningEventKind.Trust, rel.Trust - trustBefore, "Trust in " + NameOf(leader) + " " + Mathf.RoundToInt(trustBefore * 100) + "% -> " + Mathf.RoundToInt(rel.Trust * 100) + "% (led me into danger)");
            RaiseInterrupt("betrayed by leader");
        }

        public void NoteAnswer(EntityId from, float now) { _answers[from] = now; }

        public bool AnsweredSince(EntityId from, float since)
        {
            float t;
            return _answers.TryGetValue(from, out t) && t >= since;
        }

        /// <summary>If what I learned makes the current plan look hopeless, stop and reconsider (once per plan).</summary>
        private void CheckPlanConfidence(float now)
        {
            var active = Executor.ActiveStrategy;
            if (_doubtedPlan || !active.HasValue || !Cognition.PlanDoubt) return;
            float expected = Learning.Predict(active.Value).Expected;
            if (expected >= 0.2f) return;
            _doubtedPlan = true;
            Log.Event(now, "Lost confidence in plan (" + active.Value.Describe(this) + " " + Mathf.RoundToInt(expected * 100) + "%)");
            Goals.OnFailed(now, "LowConfidence");
            RaiseInterrupt("lost confidence in plan");
        }

        public void UpdateReliability(EntityId teller, float outcome, float now)
        {
            var r = Relationships.Get(teller);
            float before = r.Reliability;
            float rate = 0.35f / (1f + 0.15f * r.ClaimsChecked);
            r.Reliability = Mathf.Clamp01(r.Reliability + Mathf.Max(0.08f, rate) * (outcome - r.Reliability));
            r.ClaimsChecked++;
            Learning.Event(LearningEventKind.Trust, r.Reliability - before, NameOf(teller) + "'s information " + (outcome > 0.5f ? "confirmed" : "proved wrong") +
                           ": reliability " + Mathf.RoundToInt(before * 100) + "% -> " + Mathf.RoundToInt(r.Reliability * 100) + "%");
        }

        /// <summary>Learned dispositions move resting points of internal state (bounded; traits are untouched).</summary>
        private void ApplyDispositions()
        {
            var d = Learning.Dispositions;
            State.SetBaselineOffset(StateVar.Confidence, 0.5f * d.SelfConfidence);
            State.SetBaselineOffset(StateVar.Fear, 0.3f * Mathf.Max(0f, d.RiskPerception));
            State.SetBaselineOffset(StateVar.Curiosity, 0.4f * d.InformationSeeking);
        }

        private void SampleHistory(float now)
        {
            float topTrust = 0f;
            foreach (var r in Relationships.All) topTrust = Mathf.Max(topTrust, r.Trust);
            var latest = Log.Latest;
            History.Add(new CognitiveSample
            {
                Time = now, Emotion = Emotion.Current, Fear = State[StateVar.Fear], Stress = State[StateVar.Stress], Confidence = State[StateVar.Confidence],
                KnowledgeConfidence = Routes.ExitRouteConfidence(now), DecisionConfidence = latest != null ? latest.DecisionConfidence : 0f,
                PredictionError = Learning.LastPredictionError, Goal = Goals.Current.Type, TopTrust = topTrust
            });
        }

        public void AddPrior(FactKey key, float value, Vector3 position = default(Vector3))
            => Beliefs.Observe(key, value, 1f, 0f, BeliefSource.Prior, position);

        // ---- Body helpers ------------------------------------------------------------------------------------------

        public float SpeedMultiplier
        {
            get
            {
                float injured = World.IsCarrying(Id) ? 0.55f : 1f;
                if (IsInjured) injured = IsSupported ? 0.75f : 0.45f;
                return Emotion.Modifiers.SpeedMul * injured * (0.6f + 0.4f * State[StateVar.Energy]);
            }
        }

        /// <summary>An injured agent walking right next to the person who helped them moves faster.</summary>
        public bool IsSupported
        {
            get
            {
                var leader = Groups.LeaderOf(Id);
                return leader.IsValid && Perception.CanSee(leader) && DistanceToBelieved(leader, Now) < 3f;
            }
        }

        public void MoveBody(Vector3 target) => Body.MoveTo(target, SpeedMultiplier);

        public Vector3 RandomPointInCurrentRoom()
        {
            if (CurrentRoom < 0) return Body.Position;
            var c = FloorPlan.RoomCenter(CurrentRoom);
            var p = c + new Vector3(Rng.Range(-4f, 4f), 0f, Rng.Range(-4f, 4f));
            return FloorPlan.ClampToRoom(CurrentRoom, p, 1f);
        }

        public Vector3 FindHidingSpot()
        {
            if (CurrentRoom < 0) return Body.Position;
            var center = FloorPlan.RoomCenter(CurrentRoom);
            Vector3 best = Body.Position;
            float bestScore = float.MinValue;
            for (int i = 0; i < 4; i++)
            {
                var corner = FloorPlan.ClampToRoom(CurrentRoom, center + new Vector3(i % 2 == 0 ? -50f : 50f, 0f, i < 2 ? -50f : 50f), 0.8f);
                float score = 0f;
                var doors = FloorPlan.DoorsOf(CurrentRoom);
                for (int k = 0; k < doors.Count; k++) score += Mathf.Min(8f, Vector3.Distance(corner, FloorPlan.DoorPosition(doors[k])));
                Vector3 fire;
                if (Beliefs.TryGetPosition(FactKey.Hazard(CurrentRoom), Now, 0.2f, out fire) && Beliefs.Value(FactKey.Hazard(CurrentRoom), Now) > 0.05f)
                    score += 3f * Vector3.Distance(corner, fire);
                if (score > bestScore) { bestScore = score; best = corner; }
            }
            return best;
        }

        // ---- Social actions ----------------------------------------------------------------------------------------

        public void PerformAssist(EntityId target)
        {
            Comms.Send(MessageKind.Assist, target, 4f);
            World.PublishSocialAction(new SocialActionEvent { Actor = Id, Target = target, Kind = SocialActionKind.Helped, Position = Body.Position });
            Memory.Record(MemoryKind.HelpedSomeone, target, CurrentRoom, Now, 0.6f, 0.7f);
            Appraise(AppraisalEvent.HelpedSomeone, 1f);
            Beliefs.Observe(FactKey.Aware(target), 1f, 0.9f, Now, BeliefSource.Inferred);
            Log.Event(Now, "Helping " + NameOf(target));
        }

        public void PerformWarn(EntityId target)
        {
            Comms.SendRelevant(MessageKind.Warn, target, 8f);
            World.PublishSound(new SoundEvent { Kind = SoundKind.Shout, Position = Body.Position, Loudness = 8f, Source = Id });
            Beliefs.Observe(FactKey.Aware(target), 1f, 0.9f, Now, BeliefSource.Inferred);
            Log.Event(Now, "Warned " + NameOf(target));
        }

        public void PerformGatherCall()
        {
            Comms.Send(MessageKind.Order, EntityId.None, 12f);
            Comms.SendRelevant(MessageKind.Warn, EntityId.None, 12f);
            World.PublishSound(new SoundEvent { Kind = SoundKind.Shout, Position = Body.Position, Loudness = 12f, Source = Id });
            foreach (var id in Perception.VisibleAgents) Beliefs.Observe(FactKey.Aware(id), 1f, 0.8f, Now, BeliefSource.Inferred);
            Log.Event(Now, "Called everyone nearby to follow");
        }

        public void StopFollowing()
        {
            var leader = Groups.LeaderOf(Id);
            if (!leader.IsValid) return;
            Groups.ClearFollow(Id);
            // Being left behind while hurt by the person who was helping you is remembered.
            if (IsInjured && Memory.Has(MemoryKind.WasHelpedBy, leader) && !Perception.CanSee(leader) && !AtSafety)
            {
                Relationships.Apply(leader, SocialEventKind.AbandonedMe, Now, 0.7f);
                Memory.Record(MemoryKind.WasAbandonedBy, leader, CurrentRoom, Now, -0.8f, 0.8f);
                Appraise(AppraisalEvent.WasAbandoned, 1f);
            }
        }

        public void DropExtinguisher()
        {
            if (HeldExtinguisher < 0) return;
            World.DropExtinguisher(HeldExtinguisher, Id, Body.Position);
            HeldExtinguisher = -1;
        }

        // ---- Belief-based queries used by considerations and actions ------------------------------------------------

        /// <summary>Believed + remembered danger of a room. Never reads world truth.</summary>
        public float DangerAt(int room, float now)
        {
            if (room < 0 || FloorPlan.IsExterior(room)) return 0f;
            Belief hb;
            float hazard = Beliefs.TryGet(FactKey.Hazard(room), out hb) ? HazardSeverity(hb, now) : 0f;
            float smoke = Beliefs.Weighted(FactKey.Smoke(room), now) * 0.7f;
            float remembered = Memory.PlaceDanger(room, now) * 0.8f;
            return Mathf.Clamp01(Mathf.Max(hazard, Mathf.Max(smoke, remembered)));
        }

        private float _threatCacheTime = -1f;
        private float _threatCache;

        public float KnownThreatLevel(float now)
        {
            if (now == _threatCacheTime) return _threatCache;
            float max = 0f;
            var hazards = Beliefs.OfType(FactType.HazardInRoom);
            for (int i = 0; i < hazards.Count; i++) max = Mathf.Max(max, HazardSeverity(hazards[i], now));
            var smoke = Beliefs.OfType(FactType.SmokeInRoom);
            for (int i = 0; i < smoke.Count; i++) max = Mathf.Max(max, 0.6f * smoke[i].Value * smoke[i].EffectiveConfidence(now));
            _threatCacheTime = now;
            _threatCache = Mathf.Clamp01(max * 1.5f);
            return _threatCache;
        }

        /// <summary>Any fire is a serious threat; intensity adds to it. Weighted by how sure the agent is.</summary>
        public static float HazardSeverity(Belief b, float now)
        {
            float s = b.Value <= 0.05f ? 0f : b.EffectiveConfidence(now) * (0.4f + 0.6f * b.Value);
            // A credible competing claim of fire counts too (asymmetric caution under uncertainty).
            if (b.HasAlternative && b.AltValue > 0.05f) s = Mathf.Max(s, b.AltConfidence * (0.4f + 0.6f * b.AltValue));
            return s;
        }

        public void KnownAgents(float now, float minConfidence, List<EntityId> into)
        {
            var known = Beliefs.OfType(FactType.AgentRoom);
            for (int i = 0; i < known.Count; i++)
            {
                var b = known[i];
                if (b.Key.Subject == Id.Value) continue;
                if (b.EffectiveConfidence(now) >= minConfidence) into.Add(new EntityId(b.Key.Subject));
            }
        }

        public float DistanceToBelieved(EntityId e, float now)
        {
            Vector3 p;
            return Beliefs.TryGetPosition(FactKey.AgentRoom(e), now, 0.1f, out p)
                ? Vector3.Distance(MoveAlongRoute.Flat(Body.Position), MoveAlongRoute.Flat(p))
                : 999f;
        }

        private readonly List<int> _routeScratch = new List<int>();

        /// <summary>Whether the agent believes it can get to a room at all.</summary>
        public bool CanReachRoom(int room)
        {
            if (room < 0) return false;
            if (room == CurrentRoom) return true;
            float cost;
            return Routes.FindRouteTo(room, _routeScratch, out cost);
        }

        /// <summary>Highest believed danger among the rooms on the route to a room (including it).</summary>
        public float RouteDanger(int room, float now)
        {
            if (room < 0) return 0f;
            float cost;
            if (room != CurrentRoom && !Routes.FindRouteTo(room, _routeScratch, out cost)) return DangerAt(room, now);
            float max = Mathf.Max(DangerAt(room, now), DangerAt(CurrentRoom, now));
            int r = CurrentRoom;
            if (room != CurrentRoom)
                for (int i = 0; i < _routeScratch.Count; i++) { r = FloorPlan.DoorOtherSide(_routeScratch[i], r); max = Mathf.Max(max, DangerAt(r, now)); }
            return max;
        }

        public int BelievedRoomOf(EntityId e, float now) => (int)Beliefs.Value(FactKey.AgentRoom(e), now, -1f, 0.1f);

        public float StrongestProtectiveUrge(float now)
        {
            float empathy = 0.4f + 0.8f * Personality[Trait.Empathy];
            float aware = State[StateVar.Awareness];
            float best = 0f;
            _scratch.Clear();
            KnownAgents(now, 0.15f, _scratch);
            if (LongTermTarget.IsValid && !_scratch.Contains(LongTermTarget)) _scratch.Add(LongTermTarget);
            foreach (var id in _scratch)
            {
                if (Beliefs.Weighted(FactKey.Safe(id), now) > 0.5f) continue;
                var rel = Relationships.Peek(id);
                float affection = rel != null ? rel.Affection : 0f;
                float need = Mathf.Max(Beliefs.Weighted(FactKey.Injured(id), now), DangerAt(BelievedRoomOf(id, now), now));
                if (id == LongTermTarget && aware > 0.3f) need = Mathf.Max(need, 0.8f);
                best = Mathf.Max(best, need * (0.6f + 0.4f * affection) * empathy);
            }
            return Mathf.Clamp01(best);
        }

        public bool HasKnownExtinguisher(float now)
        {
            int id; Vector3 pos;
            return BestKnownExtinguisher(now, out id, out pos);
        }

        public bool BestKnownExtinguisher(float now, out int id, out Vector3 position)
        {
            id = -1;
            position = Vector3.zero;
            float best = float.MaxValue;
            _beliefScratch.Clear();
            Beliefs.Collect(FactType.ExtinguisherAt, _beliefScratch);
            foreach (var b in _beliefScratch)
            {
                if (b.Value < 0.5f || b.EffectiveConfidence(now) < 0.3f) continue;
                float d = Vector3.Distance(Body.Position, b.Position);
                if (d < best) { best = d; id = b.Key.Subject; position = b.Position; }
            }
            return id >= 0;
        }

        public EntityId BestPersonToAsk(float now)
        {
            EntityId best = EntityId.None;
            float bestScore = 0.15f;
            foreach (var id in Perception.VisibleAgents)
            {
                if (DistanceToBelieved(id, now) > 12f) continue;
                if (Beliefs.Weighted(FactKey.Injured(id), now) > 0.5f) continue;
                var r = Relationships.Get(id);
                float score = r.Trust - r.Suspicion + 0.3f * Memory.Impression(id);
                if (score > bestScore) { bestScore = score; best = id; }
            }
            return best;
        }

        public float UngroupedPeopleVisible(float now)
        {
            int n = 0;
            foreach (var id in Perception.VisibleAgents)
            {
                if (Groups.IsInGroupOf(id, Id)) continue;
                if (Beliefs.Weighted(FactKey.Safe(id), now) > 0.5f) continue;
                n++;
            }
            return n;
        }

        /// <summary>Long-term goals persist and bias short-term choices (ARCHITECTURE.md 7.8).</summary>
        public float LongTermBias(GoalOption option, float now)
        {
            if (!LongTermTarget.IsValid) return 1f;
            bool targetSafe = Beliefs.Weighted(FactKey.Safe(LongTermTarget), now) > 0.5f;
            if (targetSafe) return 1f;
            switch (option.Type)
            {
                case GoalType.SearchFor: return option.Target == LongTermTarget ? 1.6f : 1f;
                case GoalType.HelpOther: return option.Target == LongTermTarget ? 1.5f : 0.85f;
                case GoalType.Evacuate: return 0.6f + 0.4f * State[StateVar.Fear];
                case GoalType.Follow: return option.Target == LongTermTarget ? 1.3f : 0.8f;
                default: return 1f;
            }
        }

        /// <summary>What an observer can see this agent doing (used by the runtime to fill AgentObservation).</summary>
        public bool LooksLikeEvacuating
        {
            get
            {
                if (!Goals.HasGoal) return false;
                var t = Goals.Current.Type;
                return t == GoalType.Evacuate || t == GoalType.Follow || t == GoalType.GatherGroup;
            }
        }
    }
}
