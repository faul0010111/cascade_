using System.Collections.Generic;
using Cascade.Core;
using Cascade.World;
using UnityEngine;

namespace Cascade.Agents
{
    public enum MessageKind { Warn, Inform, Ask, Order, Assist, Reassure }

    /// <summary>
    /// Communication transfers BELIEFS, never world truth. Payloads are snapshots of the sender's own belief store,
    /// enforced by the API: there is no way to put an arbitrary fact into a message.
    /// </summary>
    public sealed class Message
    {
        public EntityId Sender;
        public MessageKind Kind;
        public EntityId Addressee;        // None = broadcast
        public float Loudness;            // meters
        public float Time;
        public Vector3 SenderPosition;
        public readonly List<Belief> Payload = new List<Belief>();
        public string Text;               // flavor only, filled by the dialogue layer

        public bool IsBroadcast => !Addressee.IsValid;
    }

    public sealed class Communicator
    {
        private readonly AgentBrain _b;
        private readonly List<Message> _outbox = new List<Message>();
        private readonly Queue<Message> _inbox = new Queue<Message>();
        private readonly List<FactKey> _keys = new List<FactKey>();

        public int MaxPayload = 12;
        public int MessagesSent { get; private set; }
        public int MessagesReceived { get; private set; }

        public Communicator(AgentBrain brain) { _b = brain; }

        public void DrainOutbox(List<Message> into)
        {
            into.AddRange(_outbox);
            _outbox.Clear();
        }

        public Message Send(MessageKind kind, EntityId addressee, float loudness, IList<FactKey> keys = null)
        {
            var m = new Message
            {
                Sender = _b.Id, Kind = kind, Addressee = addressee, Loudness = loudness,
                Time = _b.Now, SenderPosition = _b.Body.Position
            };
            if (keys != null)
            {
                for (int i = 0; i < keys.Count && m.Payload.Count < MaxPayload; i++)
                {
                    Belief b;
                    if (_b.Beliefs.TryGet(keys[i], out b) && b.EffectiveConfidence(_b.Now) >= 0.2f)
                        m.Payload.Add(b.Snapshot());
                }
            }
            _outbox.Add(m);
            MessagesSent++;
            return m;
        }

        /// <summary>Sends everything the agent believes that is relevant to the emergency.</summary>
        public Message SendRelevant(MessageKind kind, EntityId addressee, float loudness)
        {
            float now = _b.Now;
            _keys.Clear();
            foreach (var b in _b.Beliefs.All)
            {
                float conf = b.EffectiveConfidence(now);
                bool include = false;
                switch (b.Key.Type)
                {
                    case FactType.HazardInRoom: include = b.Value * conf > 0.2f; break;
                    case FactType.DoorState: include = conf > 0.4f && !DoorStates.IsPassable((DoorState)(int)b.Value); break;
                    case FactType.ExitKnown: include = b.Value > 0.5f; break;
                    case FactType.AlarmActive: include = b.Value * conf > 0.5f; break;
                    case FactType.AgentInjured:
                        include = b.Value * conf > 0.5f && b.Key.Subject != addressee.Value;
                        if (include) _keys.Add(FactKey.AgentRoom(new EntityId(b.Key.Subject)));
                        break;
                }
                if (include) _keys.Add(b.Key);
            }
            return Send(kind, addressee, loudness, _keys);
        }

        public void Receive(Message m)
        {
            if (m.Sender == _b.Id) return;
            _inbox.Enqueue(m);
        }

        public void ProcessInbox(float now)
        {
            while (_inbox.Count > 0) Process(_inbox.Dequeue(), now);
        }

        private void Process(Message m, float now)
        {
            MessagesReceived++;
            var rel = _b.Relationships.Get(m.Sender);
            // How much to believe: trust in the person, learned reliability of their information, learned trust bias.
            float trustFactor = 0.35f + 0.65f * Mathf.Clamp01(0.5f * rel.Trust + 0.5f * rel.Reliability - 0.5f * rel.Suspicion + _b.Learning.Dispositions.TrustBias);
            bool contested = false;
            bool learnedDanger = false;

            foreach (var p in m.Payload)
            {
                if (p.Key.Subject == _b.Id.Value && (p.Key.Type == FactType.AgentRoom || p.Key.Type == FactType.AgentInjured)) continue;
                float conf = p.Confidence * trustFactor * Mathf.Pow(0.85f, p.Source.Hops);
                float before = _b.Beliefs.Weighted(p.Key, now);
                var upd = _b.Beliefs.Observe(p.Key, p.Value, conf, now, BeliefSource.Told(m.Sender, p.Source.Hops + 1), p.Position);
                if (upd.Accepted && p.Key.Type == FactType.HazardInRoom && p.Value > 0.3f && before < 0.2f) learnedDanger = true;
                if (upd.Accepted && p.Key.Type == FactType.DoorState) _b.Routes.Invalidate();
                if (upd.BecameContested) contested = true;
                // Reported experience: someone says an exit is blocked. Weaker than seeing it, but it counts.
                if (upd.Accepted && upd.Changed && p.Key.Type == FactType.DoorState && _b.FloorPlan.IsExitDoor(p.Key.Subject) && !DoorStates.IsPassable((DoorState)(int)p.Value))
                    _b.Learning.Outcome(StrategyKey.Exit(p.Key.Subject), 0f, ExperienceSource.Reported, _b.NameOf(m.Sender) + " says it is blocked", m.Sender, m.Sender);
            }
            _b.Beliefs.Observe(FactKey.AgentRoom(m.Sender), _b.FloorPlan.RoomAt(m.SenderPosition), 0.7f, now, BeliefSource.Perceived, m.SenderPosition);
            if (learnedDanger) _b.Appraise(AppraisalEvent.ReceivedWarning, 0.6f);
            if (contested)
            {
                _b.Emotion.AddConflict(0.3f);
                _b.Learning.Event(LearningEventKind.Belief, 0f, _b.NameOf(m.Sender) + " contradicts what I believe");
                _b.RaiseInterrupt("conflicting information");
            }
            if (m.Kind == MessageKind.Inform) _b.NoteAnswer(m.Sender, now);
            string who = _b.NameOf(m.Sender);

            switch (m.Kind)
            {
                case MessageKind.Warn:
                    _b.Appraise(AppraisalEvent.ReceivedWarning, 1f);
                    _b.Memory.Record(MemoryKind.WasWarnedBy, m.Sender, _b.CurrentRoom, now, 0.4f, 0.6f, BeliefSourceKind.Told);
                    _b.Relationships.Apply(m.Sender, SocialEventKind.WarnedMe, now);
                    _b.Log.Event(now, who + " warned me" + (m.Payload.Count > 0 ? " (" + m.Payload.Count + " facts)" : ""));
                    _b.RaiseInterrupt("warned");
                    break;
                case MessageKind.Order:
                    _b.Beliefs.Observe(FactKey.Ordered(m.Sender), 1f, 1f, now, BeliefSource.Perceived, m.SenderPosition);
                    _b.Appraise(AppraisalEvent.OrderedByOther, 1f);
                    _b.Log.Event(now, who + " called me to follow");
                    _b.RaiseInterrupt("ordered");
                    break;
                case MessageKind.Assist:
                    _b.Relationships.Apply(m.Sender, SocialEventKind.HelpedMe, now);
                    _b.Memory.Record(MemoryKind.WasHelpedBy, m.Sender, _b.CurrentRoom, now, 0.8f, 0.9f);
                    _b.Appraise(AppraisalEvent.WasHelped, 1f);
                    _b.Beliefs.Observe(FactKey.Ordered(m.Sender), 1f, 1f, now, BeliefSource.Perceived, m.SenderPosition);
                    _b.Log.Event(now, who + " is helping me");
                    _b.RaiseInterrupt("being helped");
                    break;
                case MessageKind.Ask:
                    bool panicked = _b.Emotion.Current == EmotionState.Panicked;
                    float willingness = 0.35f + 0.5f * _b.Personality[Trait.Empathy] + 0.3f * rel.Trust;
                    if (!panicked && _b.Rng.Value() < willingness)
                    {
                        SendRelevant(MessageKind.Inform, m.Sender, 6f);
                        _b.Log.Event(now, "Told " + who + " what I know");
                    }
                    else _b.Log.Event(now, "Ignored " + who + "'s question");
                    break;
            }
            MessagesReceivedHook(m);
        }

        /// <summary>Extension point for the dialogue layer (listener shows speech bubbles, logs, etc.).</summary>
        public System.Action<Message> OnMessageProcessed;
        private void MessagesReceivedHook(Message m) { if (OnMessageProcessed != null) OnMessageProcessed(m); }
    }
}
