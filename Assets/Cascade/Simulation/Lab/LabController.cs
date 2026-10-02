using System;
using System.Collections.Generic;
using Cascade.Agents;
using Cascade.Core;
using Cascade.World;
using UnityEngine;

namespace Cascade.Simulation
{
    public enum LabCommandKind
    {
        Ignite, Extinguish, SetDoor, TriggerAlarm, Scream, Injure,
        InjectBelief, InjectRumor, Freeze, Unfreeze, ResetMind, SetTrait, Clone, SocialAct
    }

    /// <summary>A recorded experimenter action. Replaying the same commands with the same seed reproduces the run.</summary>
    [Serializable]
    public struct LabCommand
    {
        public float Time;
        public LabCommandKind Kind;
        public int Agent;        // entity id value
        public int Other;        // second entity (social acts) or trait index
        public int Int;          // door id, fact type, door state, social act kind...
        public int Subject;      // fact subject
        public float Value;
        public float Value2;
        public Vector3 Position;

        public override string ToString() => Time.ToString("0.0") + "s " + Kind + " a=" + Agent + " o=" + Other + " i=" + Int + " s=" + Subject + " v=" + Value.ToString("0.00");
    }

    /// <summary>
    /// Experimenter interface of the Simulation Lab. Every change goes through a controlled path: beliefs through
    /// belief revision (as hearsay from a "Lab" source), rumors through the communication system, harm through the
    /// damage model. Commands are logged with timestamps so a run can be replayed exactly.
    /// </summary>
    public sealed class LabController
    {
        private readonly SimulationWorld _world;
        private readonly ScenarioBuilder.BodyFactory _createBody;
        private readonly List<LabCommand> _log = new List<LabCommand>();
        private readonly List<LabCommand> _pending = new List<LabCommand>();
        private EntityId _labSource;
        private EntityId _rumorSource;

        public event Action<AgentBrain, IAgentBody> AgentSpawned;
        public IReadOnlyList<LabCommand> Log => _log;
        public string LastResult { get; private set; }

        public LabController(SimulationWorld world, ScenarioBuilder.BodyFactory createBody)
        {
            _world = world;
            _createBody = createBody;
        }

        private EntityId LabSource
        {
            get { if (!_labSource.IsValid) _labSource = _world.Context.Entities.Register(EntityKind.Interactable, "Lab"); return _labSource; }
        }

        private EntityId RumorSource
        {
            get { if (!_rumorSource.IsValid) _rumorSource = _world.Context.Entities.Register(EntityKind.Interactable, "Unknown voice"); return _rumorSource; }
        }

        /// <summary>Replays commands at their original times (call Update every frame after starting a fresh run with the same seed).</summary>
        public void ScheduleReplay(IEnumerable<LabCommand> commands)
        {
            _pending.Clear();
            _pending.AddRange(commands);
            _pending.Sort((a, b) => a.Time.CompareTo(b.Time));
        }

        public void Update()
        {
            while (_pending.Count > 0 && _pending[0].Time <= _world.Now)
            {
                Apply(_pending[0]);
                _pending.RemoveAt(0);
            }
        }

        public void Execute(LabCommand c)
        {
            c.Time = _world.Now;
            Apply(c);
        }

        private void Apply(LabCommand c)
        {
            _log.Add(c);
            var a = _world.GetAgent(new EntityId(c.Agent));
            var b = _world.Building;
            switch (c.Kind)
            {
                case LabCommandKind.Ignite:
                    LastResult = b.Hazards.Ignite(c.Position, Mathf.Max(0.2f, c.Value)) ? "Fire started" : "Nothing flammable there";
                    break;
                case LabCommandKind.Extinguish:
                    b.Hazards.Extinguish(c.Position, Mathf.Max(1f, c.Value), 1f);
                    LastResult = "Extinguished";
                    break;
                case LabCommandKind.SetDoor:
                    if (c.Int >= 0 && c.Int < b.Graph.Doors.Count) { b.SetDoorState(c.Int, (DoorState)c.Subject); LastResult = "Door " + c.Int + " -> " + (DoorState)c.Subject; }
                    break;
                case LabCommandKind.TriggerAlarm:
                    if (b.Alarms.Count > 0) b.TriggerAlarm(0);
                    LastResult = "Alarm triggered";
                    break;
                case LabCommandKind.Scream:
                    _world.PublishSound(new SoundEvent { Kind = SoundKind.Scream, Position = c.Position, Loudness = 16f });
                    LastResult = "Scream";
                    break;
                case LabCommandKind.Injure:
                    if (a != null) { a.Brain.ApplyDamage(Mathf.Max(0.1f, c.Value), _world.Now); LastResult = a.Brain.Name + " injured"; }
                    break;
                case LabCommandKind.InjectBelief:
                    if (a != null)
                    {
                        var upd = a.Brain.Beliefs.Observe(new FactKey((FactType)c.Int, c.Subject), c.Value, Mathf.Clamp01(c.Value2), _world.Now, BeliefSource.Told(LabSource, 1));
                        a.Brain.Routes.Invalidate();
                        a.Brain.RaiseInterrupt("lab: belief injected");
                        LastResult = a.Brain.Name + ": " + (FactType)c.Int + "(" + c.Subject + ") " + upd.Revision + (upd.BecameContested ? " (now contested)" : "");
                    }
                    break;
                case LabCommandKind.InjectRumor:
                    {
                        // A rumor is a message from an unknown voice: it goes through trust, hop discount and belief revision.
                        var m = new Message { Sender = RumorSource, Kind = MessageKind.Warn, Loudness = Mathf.Max(3f, c.Value2), Time = _world.Now, SenderPosition = c.Position };
                        m.Payload.Add(new Belief { Key = new FactKey((FactType)c.Int, c.Subject), Value = c.Value, Confidence = 0.9f, Time = _world.Now, Source = BeliefSource.Perceived });
                        int n = 0;
                        foreach (var ag in _world.Agents)
                            if (!ag.IsPlayer && Vector3.Distance(ag.Body.Position, c.Position) <= m.Loudness) { ag.Brain.Comms.Receive(m); n++; }
                        LastResult = "Rumor heard by " + n;
                        break;
                    }
                case LabCommandKind.Freeze:
                case LabCommandKind.Unfreeze:
                    if (a != null) { a.Frozen = c.Kind == LabCommandKind.Freeze; LastResult = a.Brain.Name + (a.Frozen ? " frozen" : " unfrozen"); }
                    break;
                case LabCommandKind.ResetMind:
                    if (a != null) { a.Brain.ResetMind(); LastResult = a.Brain.Name + ": memory, beliefs and learning reset"; }
                    break;
                case LabCommandKind.SetTrait:
                    if (a != null) { a.Brain.Personality[(Trait)c.Other] = c.Value; LastResult = a.Brain.Name + " " + (Trait)c.Other + " = " + c.Value.ToString("0.00"); }
                    break;
                case LabCommandKind.Clone:
                    if (a != null) LastResult = "Cloned as " + Clone(a, c.Value > 0.5f).Name;
                    break;
                case LabCommandKind.SocialAct:
                    {
                        var target = _world.GetAgent(new EntityId(c.Other));
                        if (a == null || target == null) break;
                        var kind = (SocialActionKind)c.Int;
                        if (kind == SocialActionKind.Helped) a.Brain.PerformAssist(target.Id);
                        else _world.PublishSocialAction(new SocialActionEvent { Actor = a.Id, Target = target.Id, Kind = kind, Position = a.Body.Position });
                        LastResult = a.Brain.Name + " " + kind + " " + target.Brain.Name;
                        break;
                    }
            }
        }

        /// <summary>Same profile (and optionally the same learned strategies and dispositions), fresh memories and relationships.</summary>
        public AgentBrain Clone(SimAgent source, bool withLearning)
        {
            var p = source.Brain.Profile;
            var profile = new AgentProfile
            {
                Name = p.Name + "'", Archetype = p.Archetype, Personality = p.Personality.Clone(), Background = p.Background,
                SpeechStyle = p.SpeechStyle, KnowsAllExits = p.KnowsAllExits
            };
            var pos = source.Body.Position + new Vector3(1.2f, 0f, 0f);
            var id = _world.Context.Entities.Register(EntityKind.Npc, profile.Name);
            var body = _createBody(id, profile, pos);
            var brain = new AgentBrain(id, profile, _world.Services, body);
            AgentFactory.ApplyPriors(brain, _world.Building.Graph, ScenarioBuilder.FindMainExit(_world.Building.Graph, "Front Yard"));
            if (withLearning) brain.Learning.CopyFrom(source.Brain.Learning);
            _world.Register(brain, body, false);
            if (AgentSpawned != null) AgentSpawned(brain, body);
            return brain;
        }
    }
}
