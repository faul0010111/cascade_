using System.Collections.Generic;
using System.Diagnostics;
using Cascade.Agents;
using Cascade.Core;
using Cascade.World;
using UnityEngine;

namespace Cascade.Simulation
{
    /// <summary>Line-of-sight provider: Unity physics in the game, a room-graph approximation headless.</summary>
    public interface ILineOfSight
    {
        bool Check(Vector3 from, Vector3 to);
    }

    public sealed class SimAgent
    {
        public EntityId Id;
        public AgentBrain Brain;
        public IAgentBody Body;
        public bool IsPlayer;
        public float NextTick;
        public float LastTick;
        public LodTier Lod;
        public float LastTickMs;
        /// <summary>Frozen agents are not ticked (Simulation Lab).</summary>
        public bool Frozen;
    }

    public struct SimulationStats
    {
        public int Agents, Safe, Injured, Incapacitated, InsideBuilding;
        public float AvgTickMs, MaxTickMs;
        public int TicksThisFrame, DeferredThisFrame;
        public int MessagesDelivered;
        public float FirstEvacuationTime, LastEvacuationTime;
    }

    /// <summary>
    /// The engine-independent simulation: world truth, agent registry, event routing (sound, messages, witnessed
    /// social acts), hazard damage and the time-sliced LOD scheduler. Unity and the headless runner both drive it.
    /// It is also the ONLY implementation of IPerceptionSource and IAgentWorldActions.
    /// </summary>
    public sealed class SimulationWorld : IPerceptionSource, IAgentWorldActions
    {
        public readonly SimulationContext Context;
        public readonly BuildingModel Building;
        public readonly GroupRegistry Groups = new GroupRegistry();
        public readonly AgentServices Services;
        private readonly ILineOfSight _los;
        private readonly SpatialHash<EntityId> _spatial = new SpatialHash<EntityId>(4f);
        private readonly Dictionary<EntityId, SimAgent> _agents = new Dictionary<EntityId, SimAgent>();
        private readonly List<SimAgent> _order = new List<SimAgent>();
        private readonly List<EntityId> _query = new List<EntityId>();
        private readonly List<Message> _messages = new List<Message>();
        private readonly Stopwatch _watch = new Stopwatch();
        private readonly Dictionary<EntityId, EntityId> _carriedBy = new Dictionary<EntityId, EntityId>();

        // LOD and scheduling
        public readonly List<Vector3> FocusPoints = new List<Vector3>();
        public EntityId SelectedAgent;
        public float FullLodRadius = 18f;
        public float ReducedLodRadius = 40f;
        public float FrameBudgetMs = 4f;
        public float HazardStepInterval = 0.2f;
        public bool LodEnabled = true;
        public bool AlarmWorks = true;

        public SimulationStats Stats;
        public event System.Action<Message, EntityId> MessageDelivered;
        public event System.Action<Message> MessageSent;
        public event System.Action<SocialActionEvent> SocialActionPublished;
        public event System.Action<SoundEvent> SoundPublished;

        private float _hazardAccumulator;
        private float _nextAlarmPulse;
        private int _roundRobin;
        private int _messagesDelivered;
        private float _firstEvac = -1f, _lastEvac = -1f;

        public SimulationWorld(int seed, RoomGraph graph, ILineOfSight los)
        {
            Context = new SimulationContext(seed);
            Building = new BuildingModel(graph, Context.Bus, Context.CreateRng("hazards"));
            _los = los;
            Services = new AgentServices
            {
                Sim = Context, FloorPlan = graph, Perception = this, WorldActions = this, Groups = Groups
            };
            Context.Bus.Subscribe<SoundEvent>(RouteSound);
            Context.Bus.Subscribe<AlarmTriggeredEvent>(e => _nextAlarmPulse = Context.Clock.Now);
        }

        public float Now => Context.Clock.Now;
        public IReadOnlyList<SimAgent> Agents => _order;

        public SimAgent GetAgent(EntityId id)
        {
            SimAgent a;
            return _agents.TryGetValue(id, out a) ? a : null;
        }

        public SimAgent Register(AgentBrain brain, IAgentBody body, bool isPlayer)
        {
            var a = new SimAgent { Id = brain.Id, Brain = brain, Body = body, IsPlayer = isPlayer, NextTick = Now };
            _agents[brain.Id] = a;
            _order.Add(a);
            _spatial.Update(brain.Id, body.Position);
            return a;
        }

        public void Unregister(EntityId id)
        {
            SimAgent a;
            if (!_agents.TryGetValue(id, out a)) return;
            _agents.Remove(id);
            _order.Remove(a);
            _spatial.Remove(id);
            Groups.Remove(id);
            Context.Entities.Unregister(id);
        }

        // ---- Main step ---------------------------------------------------------------------------------------------

        /// <summary>Advances the simulation. Returns the simulated delta.</summary>
        public float Step(float realDelta)
        {
            float dt = Context.Clock.Advance(realDelta);
            if (dt <= 0f) return 0f;
            float now = Now;

            _hazardAccumulator += dt;
            if (_hazardAccumulator >= HazardStepInterval)
            {
                Building.Step(_hazardAccumulator);
                _hazardAccumulator = 0f;
            }
            if (AlarmWorks && Building.AnyAlarmActive && now >= _nextAlarmPulse)
            {
                _nextAlarmPulse = now + 4f;
                foreach (var alarm in Building.Alarms)
                    RouteSound(new SoundEvent { Kind = SoundKind.Alarm, Position = alarm.Position, Loudness = 45f });
            }

            UpdateCarrying();
            foreach (var a in _order)
            {
                _spatial.Update(a.Id, a.Body.Position);
                if (a.IsPlayer) continue;
                var sample = Building.Hazards.Sample(a.Body.Position);
                a.Brain.ApplyDamage(HazardDamage.Compute(sample, dt), now);
            }

            RunScheduler(now);
            RouteMessages();
            Context.Bus.FlushDeferred();
            UpdateStats();
            return dt;
        }

        private void RunScheduler(float now)
        {
            Stats.TicksThisFrame = 0;
            Stats.DeferredThisFrame = 0;
            int n = _order.Count;
            if (n == 0) return;
            _watch.Reset();
            _watch.Start();
            for (int k = 0; k < n; k++)
            {
                var a = _order[(_roundRobin + k) % n];
                a.Lod = ComputeLod(a);
                if (a.Frozen) { a.Body.Stop(); continue; }
                if (now < a.NextTick && !a.Brain.InterruptPending) continue;
                if (_watch.Elapsed.TotalMilliseconds > FrameBudgetMs && Stats.TicksThisFrame > 0)
                {
                    Stats.DeferredThisFrame++;
                    continue;
                }
                long before = _watch.ElapsedTicks;
                float agentDt = a.LastTick > 0f ? now - a.LastTick : 0.1f;
                a.Brain.Tick(now, Mathf.Max(0.001f, agentDt), a.Lod);
                a.LastTick = now;
                a.NextTick = now + TickInterval(a.Lod);
                a.LastTickMs = (float)((_watch.ElapsedTicks - before) * 1000.0 / Stopwatch.Frequency);
                Stats.TicksThisFrame++;
            }
            _roundRobin = (_roundRobin + 1) % n;
        }

        public static float TickInterval(LodTier lod) => lod == LodTier.Full ? 0.1f : lod == LodTier.Reduced ? 0.25f : 0.5f;

        private LodTier ComputeLod(SimAgent a)
        {
            if (!LodEnabled || a.IsPlayer || a.Id == SelectedAgent || a.Brain.InterruptPending) return LodTier.Full;
            if (FocusPoints.Count == 0) return LodTier.Full;
            float best = float.MaxValue;
            foreach (var p in FocusPoints) best = Mathf.Min(best, Vector3.Distance(p, a.Body.Position));
            // Agents in immediate danger are always simulated at full fidelity.
            if (a.Brain.Emotion.Current == EmotionState.Panicked || a.Brain.IsInjured) return LodTier.Full;
            if (best < FullLodRadius) return LodTier.Full;
            return best < ReducedLodRadius ? LodTier.Reduced : LodTier.Minimal;
        }

        // ---- Routing -----------------------------------------------------------------------------------------------

        private void RouteSound(SoundEvent s)
        {
            _query.Clear();
            _spatial.Query(s.Position, s.Loudness, _query);
            foreach (var id in _query)
            {
                SimAgent a;
                if (_agents.TryGetValue(id, out a)) a.Brain.Perception.Hear(s);
            }
            if (SoundPublished != null) SoundPublished(s);
        }

        private void RouteMessages()
        {
            _messages.Clear();
            foreach (var a in _order) a.Brain.Comms.DrainOutbox(_messages);
            foreach (var m in _messages)
            {
                if (MessageSent != null) MessageSent(m);
                if (!m.IsBroadcast)
                {
                    SimAgent target;
                    if (_agents.TryGetValue(m.Addressee, out target) &&
                        Vector3.Distance(target.Body.Position, m.SenderPosition) <= m.Loudness * 1.5f)
                        Deliver(m, target);
                    continue;
                }
                _query.Clear();
                _spatial.Query(m.SenderPosition, m.Loudness, _query);
                int senderRoom = Building.Graph.RoomAt(m.SenderPosition);
                foreach (var id in _query)
                {
                    if (id == m.Sender) continue;
                    SimAgent a;
                    if (!_agents.TryGetValue(id, out a)) continue;
                    float d = Vector3.Distance(a.Body.Position, m.SenderPosition);
                    bool sameRoom = Building.Graph.RoomAt(a.Body.Position) == senderRoom;
                    if (!sameRoom && d > m.Loudness * 0.6f && !_los.Check(m.SenderPosition + Vector3.up * 1.5f, a.Body.Position + Vector3.up * 1.5f)) continue;
                    Deliver(m, a);
                }
            }
        }

        private void Deliver(Message m, SimAgent receiver)
        {
            receiver.Brain.Comms.Receive(m);
            _messagesDelivered++;
            if (MessageDelivered != null) MessageDelivered(m, receiver.Id);
        }

        // ---- Stats -------------------------------------------------------------------------------------------------

        private void UpdateStats()
        {
            int safe = 0, injured = 0, down = 0, npcs = 0;
            float total = 0f, max = 0f;
            foreach (var a in _order)
            {
                if (a.IsPlayer) continue;
                npcs++;
                if (a.Brain.AtSafety)
                {
                    safe++;
                    if (_firstEvac < 0f) _firstEvac = Now;
                }
                if (a.Brain.Incapacitated) down++;
                else if (a.Brain.IsInjured) injured++;
                total += a.LastTickMs;
                max = Mathf.Max(max, a.LastTickMs);
            }
            if (npcs > 0 && safe + down == npcs && _lastEvac < 0f) _lastEvac = Now;
            Stats.Agents = npcs;
            Stats.Safe = safe;
            Stats.Injured = injured;
            Stats.Incapacitated = down;
            Stats.InsideBuilding = npcs - safe;
            Stats.AvgTickMs = npcs > 0 ? total / npcs : 0f;
            Stats.MaxTickMs = max;
            Stats.MessagesDelivered = _messagesDelivered;
            Stats.FirstEvacuationTime = _firstEvac;
            Stats.LastEvacuationTime = _lastEvac;
        }

        // ---- IPerceptionSource (the only door from truth to agents) ------------------------------------------------

        public void QueryAgents(Vector3 center, float radius, List<EntityId> results) => _spatial.Query(center, radius, results);

        public bool TryObserveAgent(EntityId id, out AgentObservation observation)
        {
            SimAgent a;
            if (!_agents.TryGetValue(id, out a)) { observation = default(AgentObservation); return false; }
            observation = new AgentObservation
            {
                Id = id,
                Position = a.Body.Position,
                Injured = a.Brain.IsInjured,
                Incapacitated = a.Brain.Incapacitated,
                Evacuating = a.IsPlayer ? PlayerLooksEvacuating : a.Brain.LooksLikeEvacuating,
                IsPlayer = a.IsPlayer
            };
            return true;
        }

        /// <summary>Set by the player controller when the player is moving toward an exit.</summary>
        public bool PlayerLooksEvacuating;

        public bool HasLineOfSight(Vector3 from, Vector3 to) => _los.Check(from, to);

        public HazardSample SampleHazards(Vector3 position) => Building.Hazards.Sample(position);

        public float ObserveRoomFire(int room, Vector3 viewer, float range, out Vector3 firePosition)
        {
            float fire = Building.Hazards.RoomFire(room, out firePosition);
            if (fire <= 0f) return 0f;
            // Flames are bright: visible slightly beyond normal range, but still need line of sight.
            // -1 means "could not tell": the agent must not conclude there is no fire.
            if (Vector3.Distance(viewer, firePosition) > range * 1.5f) return -1f;
            if (!_los.Check(viewer, firePosition + Vector3.up * 0.5f)) return -1f;
            return fire;
        }

        public float ObserveRoomSmoke(int room) => Building.Hazards.RoomSmoke(room);

        public DoorState ObserveDoor(int door) => Building.Graph.Doors[door].State;

        public void QueryExtinguishers(int room, List<ExtinguisherObservation> results)
        {
            foreach (var e in Building.Extinguishers)
            {
                if (e.HeldBy.IsValid || Building.Graph.RoomAt(e.Position) != room) continue;
                results.Add(new ExtinguisherObservation { Id = e.Id, Position = e.Position, Available = e.Available });
            }
        }

        // ---- IAgentWorldActions -----------------------------------------------------------------------------------

        public bool TryTakeExtinguisher(int id, EntityId agent)
        {
            if (id < 0 || id >= Building.Extinguishers.Count) return false;
            var e = Building.Extinguishers[id];
            if (!e.Available) return false;
            SimAgent a;
            if (_agents.TryGetValue(agent, out a) && Vector3.Distance(a.Body.Position, e.Position) > 2.5f) return false;
            e.HeldBy = agent;
            return true;
        }

        public void DropExtinguisher(int id, EntityId agent, Vector3 position)
        {
            if (id < 0 || id >= Building.Extinguishers.Count) return;
            var e = Building.Extinguishers[id];
            if (e.HeldBy != agent) return;
            e.HeldBy = EntityId.None;
            e.Position = position;
            e.Room = Building.Graph.RoomAt(position);
        }

        public float UseExtinguisher(int id, Vector3 target, float dt)
        {
            if (id < 0 || id >= Building.Extinguishers.Count) return 1f;
            var e = Building.Extinguishers[id];
            if (e.Charge <= 0f) return Building.Hazards.Extinguish(target, 0f, 0f);
            e.Charge = Mathf.Max(0f, e.Charge - dt / 14f);
            return Building.Hazards.Extinguish(target, 2.2f, 0.35f * dt);
        }

        public float ExtinguisherCharge(int id) => id >= 0 && id < Building.Extinguishers.Count ? Building.Extinguishers[id].Charge : 0f;

        public void PublishSocialAction(SocialActionEvent evt)
        {
            _query.Clear();
            _spatial.Query(evt.Position, 15f, _query);
            foreach (var id in _query)
            {
                SimAgent a;
                if (_agents.TryGetValue(id, out a)) a.Brain.Perception.Witness(evt);
            }
            if (SocialActionPublished != null) SocialActionPublished(evt);
        }

        public void PublishSound(SoundEvent evt) => RouteSound(evt);

        public bool TryStartCarry(EntityId carrier, EntityId target)
        {
            SimAgent c = GetAgent(carrier), t = GetAgent(target);
            if (c == null || t == null || _carriedBy.ContainsKey(target) || _carriedBy.ContainsKey(carrier)) return false;
            if (Vector3.Distance(c.Body.Position, t.Body.Position) > 2.5f) return false;
            _carriedBy[target] = carrier;
            return true;
        }

        public void StopCarry(EntityId carrier)
        {
            EntityId found = EntityId.None;
            foreach (var kv in _carriedBy) if (kv.Value == carrier) { found = kv.Key; break; }
            if (found.IsValid) _carriedBy.Remove(found);
        }

        public bool IsCarrying(EntityId carrier)
        {
            foreach (var kv in _carriedBy) if (kv.Value == carrier) return true;
            return false;
        }

        public bool IsBeingCarried(EntityId target) => _carriedBy.ContainsKey(target);

        private readonly List<EntityId> _carryScratch = new List<EntityId>();

        private void UpdateCarrying()
        {
            _carryScratch.Clear();
            foreach (var kv in _carriedBy)
            {
                SimAgent carrier = GetAgent(kv.Value), carried = GetAgent(kv.Key);
                if (carrier == null || carried == null || carrier.Brain.Incapacitated) { _carryScratch.Add(kv.Key); continue; }
                carried.Body.Teleport(carrier.Body.Position - carrier.Body.Forward * 0.7f);
            }
            foreach (var id in _carryScratch) _carriedBy.Remove(id);
        }
    }
}
