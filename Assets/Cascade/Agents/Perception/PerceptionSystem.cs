using System.Collections.Generic;
using Cascade.Core;
using Cascade.World;
using UnityEngine;

namespace Cascade.Agents
{
    /// <summary>
    /// Converts world stimuli into beliefs, appraisals and memories. Decides WHAT is visible first (current room plus
    /// rooms seen through open doors, vision cone, line of sight) and only then asks the world about those things.
    /// Stress narrows the vision cone and smoke shortens range: emotion changes behavior through perception.
    /// </summary>
    public sealed class PerceptionSystem
    {
        public float VisionRange = 14f;
        public float FovDegrees = 150f;
        public float EyeHeight = 1.6f;
        public float ProximityRadius = 2.5f;

        public float EffectiveRange { get; private set; }
        public float EffectiveFov { get; private set; }

        private readonly AgentBrain _b;
        private readonly IPerceptionSource _src;
        private readonly List<int> _visibleRooms = new List<int>();
        private readonly List<EntityId> _visibleAgents = new List<EntityId>();
        private readonly List<EntityId> _query = new List<EntityId>();
        private readonly List<ExtinguisherObservation> _extinguishers = new List<ExtinguisherObservation>();
        private readonly Queue<SoundEvent> _sounds = new Queue<SoundEvent>();
        private readonly Queue<SocialActionEvent> _witnessed = new Queue<SocialActionEvent>();
        private float _lastSense = -1f;
        private readonly HashSet<EntityId> _seenEscape = new HashSet<EntityId>();

        private static int DoorBetween(IFloorPlan plan, int a, int b)
        {
            var doors = plan.DoorsOf(a);
            for (int i = 0; i < doors.Count; i++) if (plan.DoorOtherSide(doors[i], a) == b) return doors[i];
            return -1;
        }

        public PerceptionSystem(AgentBrain brain, IPerceptionSource source)
        {
            _b = brain;
            _src = source;
            EffectiveRange = VisionRange;
            EffectiveFov = FovDegrees;
        }

        public IReadOnlyList<int> VisibleRooms => _visibleRooms;
        public IReadOnlyList<EntityId> VisibleAgents => _visibleAgents;
        public bool CanSee(EntityId id) => _visibleAgents.Contains(id);

        public void Hear(SoundEvent evt) { _sounds.Enqueue(evt); }
        public void Witness(SocialActionEvent evt) { _witnessed.Enqueue(evt); }

        public void Sense(float now)
        {
            var plan = _b.FloorPlan;
            var pos = _b.Body.Position;
            var eye = pos + Vector3.up * EyeHeight;
            int room = plan.RoomAt(pos);
            _b.SetRoom(room, now);
            float dtScale = _lastSense < 0f ? 0.2f : Mathf.Clamp(now - _lastSense, 0.05f, 1f);
            _lastSense = now;

            var felt = _src.SampleHazards(pos);
            EffectiveRange = VisionRange * (1f - 0.6f * felt.Smoke);
            EffectiveFov = FovDegrees * _b.Emotion.Modifiers.FovMul * (1f - 0.3f * _b.State[StateVar.Stress]);

            _visibleRooms.Clear();
            if (room >= 0)
            {
                _visibleRooms.Add(room);
                var doors = plan.DoorsOf(room);
                for (int k = 0; k < doors.Count; k++)
                {
                    int d = doors[k];
                    var doorPos = plan.DoorPosition(d);
                    if (Vector3.Distance(pos, doorPos) > EffectiveRange) continue;
                    if (!_src.HasLineOfSight(eye, doorPos + Vector3.up * 1.2f)) continue;
                    var state = ObserveDoor(d, now);
                    if (state == DoorState.Open || state == DoorState.Blocked)
                    {
                        int other = plan.DoorOtherSide(d, room);
                        if (!_visibleRooms.Contains(other)) _visibleRooms.Add(other);
                    }
                }
            }

            for (int i = 0; i < _visibleRooms.Count; i++) ObserveRoom(_visibleRooms[i], eye, now, _visibleRooms[i] == room);
            FeelHazards(felt, room, now, dtScale);
            ObserveAgents(pos, eye, now);
        }

        private DoorState ObserveDoor(int door, float now)
        {
            var plan = _b.FloorPlan;
            var state = _src.ObserveDoor(door);
            var upd = _b.Beliefs.Observe(FactKey.Door(door), (float)(int)state, 0.95f, now, BeliefSource.Perceived);
            _b.HandleVerification(upd, now);

            if (plan.IsExitDoor(door))
            {
                var e = _b.Beliefs.Observe(FactKey.Exit(door), 1f, 1f, now, BeliefSource.Perceived);
                if (e.IsNew || e.OldWeighted < 0.5f) _b.Log.Event(now, "Noticed an exit (" + plan.RoomName(plan.DoorOtherSide(door, _b.CurrentRoom)) + ")");
            }

            bool nowImpassable = !DoorStates.IsPassable(state);
            bool wasPassable = upd.IsNew || DoorStates.IsPassable((DoorState)(int)upd.OldValue);
            if (!nowImpassable && !wasPassable && plan.IsExitDoor(door))
                _b.Learning.Reconsider(StrategyKey.Exit(door), 0.4f, "seen open again");
            if (upd.Revision == RevisionKind.Revised && !upd.IsNew)
                _b.Learning.Event(LearningEventKind.Belief, 0f, "Revised: door " + door + " is " + state.ToString().ToLowerInvariant() + " (" + upd.Belief.Contradictions + " contradictions)");
            if (nowImpassable && wasPassable)
            {
                _b.Memory.Record(MemoryKind.DoorWasBlocked, EntityId.None, _b.CurrentRoom, now, -0.4f, state == DoorState.Blocked ? 0.8f : 0.4f);
                _b.Log.Event(now, "Door to " + plan.RoomName(plan.DoorOtherSide(door, _b.CurrentRoom)) + " is " + state.ToString().ToLowerInvariant());
                _b.Routes.Invalidate();
                if (_b.IsDoorOnCurrentRoute(door))
                {
                    _b.Appraise(AppraisalEvent.RouteBlocked, 1f);
                    _b.RaiseInterrupt("route blocked");
                }
            }
            return state;
        }

        private void ObserveRoom(int room, Vector3 eye, float now, bool isCurrent)
        {
            var plan = _b.FloorPlan;
            if (plan.IsExterior(room)) return;

            Vector3 firePos;
            float fire = _src.ObserveRoomFire(room, eye, EffectiveRange, out firePos);
            if (fire < 0f) { ObserveRoomContents(room, now, isCurrent); return; } // fire exists but is out of sight: no conclusion
            float before = _b.Beliefs.Weighted(FactKey.Hazard(room), now);
            var upd = _b.Beliefs.Observe(FactKey.Hazard(room), fire, isCurrent ? 0.95f : 0.8f, now, BeliefSource.Perceived,
                                         fire > 0f ? firePos : default(Vector3));
            _b.HandleVerification(upd, now);
            if (fire > 0.05f && before < 0.2f && !RecentlySawFire(room, now))
            {
                float dist = Vector3.Distance(_b.Body.Position, firePos);
                bool close = dist < 7f;
                _b.Appraise(close ? AppraisalEvent.SawFireClose : AppraisalEvent.SawFireFar, 1f);
                _b.Memory.Record(MemoryKind.SawFire, EntityId.None, room, now, -0.8f, Mathf.Clamp01(0.4f + fire));
                _b.Log.Event(now, "Saw fire in " + plan.RoomName(room) + (close ? " (close!)" : ""));
                _b.RaiseInterrupt("saw fire");
            }

            ObserveRoomContents(room, now, isCurrent);
        }

        private void ObserveRoomContents(int room, float now, bool isCurrent)
        {
            _b.Beliefs.Observe(FactKey.Smoke(room), _src.ObserveRoomSmoke(room), isCurrent ? 0.95f : 0.7f, now, BeliefSource.Perceived);

            _extinguishers.Clear();
            _src.QueryExtinguishers(room, _extinguishers);
            for (int i = 0; i < _extinguishers.Count; i++)
            {
                var e = _extinguishers[i];
                if (Vector3.Distance(_b.Body.Position, e.Position) > EffectiveRange) continue;
                _b.Beliefs.Observe(FactKey.Extinguisher(e.Id), e.Available ? 1f : 0f, 0.9f, now, BeliefSource.Perceived, e.Position);
            }
        }

        private bool RecentlySawFire(int room, float now)
        {
            var episodes = _b.Memory.Episodes;
            for (int i = episodes.Count - 1; i >= 0; i--)
                if (episodes[i].Kind == MemoryKind.SawFire && episodes[i].Place == room && now - episodes[i].Time < 30f) return true;
            return false;
        }

        private void FeelHazards(HazardSample felt, int room, float now, float dtScale)
        {
            if (felt.Smoke > 0.12f) _b.Appraise(AppraisalEvent.InSmoke, felt.Smoke * dtScale);
            if (felt.Heat > 0.3f) _b.Appraise(AppraisalEvent.NearHeat, felt.Heat * dtScale);
            if (room >= 0 && (felt.Heat > 0.4f || felt.Smoke > 0.35f))
                _b.Memory.Record(MemoryKind.RouteDangerous, EntityId.None, room, now, -0.6f, Mathf.Max(felt.Heat, felt.Smoke));
        }

        private void ObserveAgents(Vector3 pos, Vector3 eye, float now)
        {
            var plan = _b.FloorPlan;
            _query.Clear();
            _visibleAgents.Clear();
            _src.QueryAgents(pos, EffectiveRange, _query);
            for (int i = 0; i < _query.Count; i++)
            {
                var id = _query[i];
                if (id == _b.Id) continue;
                AgentObservation o;
                if (!_src.TryObserveAgent(id, out o)) continue;

                var to = o.Position - pos;
                to.y = 0f;
                float dist = to.magnitude;
                bool near = dist < ProximityRadius;
                if (!near && Vector3.Angle(_b.Body.Forward, to) > EffectiveFov * 0.5f) continue;
                int theirRoom = plan.RoomAt(o.Position);
                if (!near && !_visibleRooms.Contains(theirRoom)) continue;
                if (!near && !_src.HasLineOfSight(eye, o.Position + Vector3.up * 1.4f)) continue;

                _visibleAgents.Add(id);
                var beliefs = _b.Beliefs;
                int roomBefore = (int)beliefs.Value(FactKey.AgentRoom(id), now, -1f, 0.2f);
                if (roomBefore >= 0 && !plan.IsExterior(roomBefore) && plan.IsExterior(theirRoom) && _seenEscape.Add(id))
                {
                    int door = DoorBetween(plan, roomBefore, theirRoom);
                    if (door >= 0) _b.Learning.Outcome(StrategyKey.Exit(door), 1f, ExperienceSource.Observed, "saw " + _b.NameOf(id) + " get out", id, id);
                }
                bool injured = o.Injured || o.Incapacitated;
                float injuredBefore = beliefs.Weighted(FactKey.Injured(id), now);
                beliefs.Observe(FactKey.AgentRoom(id), theirRoom, 1f, now, BeliefSource.Perceived, o.Position);
                var injUpd = beliefs.Observe(FactKey.Injured(id), injured ? 1f : 0f, 0.9f, now, BeliefSource.Perceived);
                _b.HandleVerification(injUpd, now);
                beliefs.Observe(FactKey.Safe(id), plan.IsExterior(theirRoom) ? 1f : 0f, 0.9f, now, BeliefSource.Perceived);
                beliefs.Observe(FactKey.Evacuating(id), o.Evacuating ? 1f : 0f, 0.8f, now, BeliefSource.Perceived);
                if (o.Evacuating || plan.IsExterior(theirRoom)) beliefs.Observe(FactKey.Aware(id), 1f, 0.8f, now, BeliefSource.Inferred);
                _b.Relationships.Get(id); // meeting someone creates a relationship record
                _b.NoteIncapacitated(id, o.Incapacitated);

                if (injured && injuredBefore < 0.4f)
                {
                    _b.Appraise(AppraisalEvent.SawInjured, 1f);
                    _b.Memory.Record(MemoryKind.WitnessedInjury, id, theirRoom, now, -0.3f, 0.7f);
                    _b.Log.Event(now, "Saw " + _b.NameOf(id) + " injured");
                    _b.RaiseInterrupt("saw someone injured");
                }
            }
        }

        public void ProcessQueued(float now)
        {
            var plan = _b.FloorPlan;
            var pos = _b.Body.Position;
            while (_sounds.Count > 0)
            {
                var s = _sounds.Dequeue();
                if (s.Source == _b.Id) continue;
                int sourceRoom = plan.RoomAt(s.Position);
                float attenuation = sourceRoom == _b.CurrentRoom ? 1f : 0.6f;
                if (Vector3.Distance(s.Position, pos) > s.Loudness * attenuation) continue;

                if (s.Kind == SoundKind.Alarm)
                {
                    float before = _b.Beliefs.Weighted(FactKey.Alarm, now);
                    _b.Beliefs.Observe(FactKey.Alarm, 1f, 1f, now, BeliefSource.Perceived, s.Position);
                    if (before < 0.5f)
                    {
                        _b.Appraise(AppraisalEvent.HeardAlarm, 1f);
                        _b.Log.Event(now, "Heard the fire alarm");
                        _b.RaiseInterrupt("alarm");
                    }
                    continue;
                }

                _b.Beliefs.Observe(FactKey.Noise(sourceRoom), Mathf.Clamp01(s.Loudness / 15f), 0.8f, now, BeliefSource.Perceived, s.Position);
                if (s.Source.IsValid)
                    _b.Beliefs.Observe(FactKey.AgentRoom(s.Source), sourceRoom, 0.5f, now, BeliefSource.Perceived, s.Position);
                if (s.Kind == SoundKind.Scream)
                {
                    _b.Appraise(AppraisalEvent.ReceivedWarning, 0.4f);
                    _b.Log.Event(now, "Heard a scream from " + plan.RoomName(sourceRoom));
                }
            }

            while (_witnessed.Count > 0)
            {
                var e = _witnessed.Dequeue();
                if (e.Actor == _b.Id) continue;
                if (e.Target == _b.Id && e.Kind == SocialActionKind.Pushed)
                {
                    _b.Relationships.Apply(e.Actor, SocialEventKind.PushedMe, now);
                    _b.Memory.Record(MemoryKind.WasPushedBy, e.Actor, _b.CurrentRoom, now, -0.7f, 0.8f);
                    _b.Appraise(AppraisalEvent.Pushed, 1f);
                    _b.Log.Event(now, _b.NameOf(e.Actor) + " pushed me");
                    continue;
                }
                if (e.Target == _b.Id) continue; // direct outcomes are handled by the target itself
                if (!CanSee(e.Actor) && Vector3.Distance(e.Position, pos) > 4f) continue;
                switch (e.Kind)
                {
                    case SocialActionKind.Helped:
                        _b.Relationships.Apply(e.Actor, SocialEventKind.HelpedOther, now);
                        _b.Memory.Record(MemoryKind.SawSomeoneHelp, e.Actor, _b.CurrentRoom, now, 0.5f, 0.5f);
                        break;
                    case SocialActionKind.Pushed:
                        _b.Relationships.Apply(e.Actor, SocialEventKind.PushedMe, now, 0.35f);
                        break;
                    case SocialActionKind.Abandoned:
                        _b.Relationships.Apply(e.Actor, SocialEventKind.AbandonedMe, now, 0.3f);
                        break;
                    case SocialActionKind.Endangered: // saw someone get hurt following e.Actor
                        _b.Relationships.Apply(e.Actor, SocialEventKind.LedMeIntoDanger, now, 0.35f);
                        _b.Learning.Outcome(StrategyKey.Follow(e.Actor), 0f, ExperienceSource.Observed, _b.NameOf(e.Target) + " got hurt following them", e.Actor, e.Target);
                        break;
                    case SocialActionKind.Led:
                        _b.Relationships.Apply(e.Actor, SocialEventKind.LedMeToSafety, now, 0.35f);
                        _b.Learning.Outcome(StrategyKey.Follow(e.Actor), 1f, ExperienceSource.Observed, "led " + _b.NameOf(e.Target) + " out", e.Actor, e.Target);
                        break;
                }
            }
        }
    }
}
