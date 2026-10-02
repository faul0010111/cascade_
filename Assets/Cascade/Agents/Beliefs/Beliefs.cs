using System;
using System.Collections.Generic;
using Cascade.Core;
using UnityEngine;

namespace Cascade.Agents
{
    public enum FactType
    {
        HazardInRoom,     // value: fire intensity 0..1, position: hottest seen point
        SmokeInRoom,      // value: density 0..1
        DoorState,        // value: (float)World.DoorState
        ExitKnown,        // subject: door id, value 1
        AgentRoom,        // subject: entity id, value: room id, position: last known position
        AgentInjured,     // value 0/1
        AgentSafe,        // value 0/1 (believed outside)
        AgentEvacuating,  // value 0/1
        AgentAware,       // value 0/1 (believed to know about the emergency)
        ExtinguisherAt,   // subject: extinguisher id, value: 1 available / 0 gone, position
        AlarmActive,      // subject 0
        NoiseInRoom,      // value: loudness, position
        OrderedToFollow   // subject: entity who told me to follow
    }

    public readonly struct FactKey : IEquatable<FactKey>
    {
        public readonly FactType Type;
        public readonly int Subject;

        public FactKey(FactType type, int subject) { Type = type; Subject = subject; }

        public static FactKey Hazard(int room) => new FactKey(FactType.HazardInRoom, room);
        public static FactKey Smoke(int room) => new FactKey(FactType.SmokeInRoom, room);
        public static FactKey Door(int door) => new FactKey(FactType.DoorState, door);
        public static FactKey Exit(int door) => new FactKey(FactType.ExitKnown, door);
        public static FactKey AgentRoom(EntityId e) => new FactKey(FactType.AgentRoom, e.Value);
        public static FactKey Injured(EntityId e) => new FactKey(FactType.AgentInjured, e.Value);
        public static FactKey Safe(EntityId e) => new FactKey(FactType.AgentSafe, e.Value);
        public static FactKey Evacuating(EntityId e) => new FactKey(FactType.AgentEvacuating, e.Value);
        public static FactKey Aware(EntityId e) => new FactKey(FactType.AgentAware, e.Value);
        public static FactKey Extinguisher(int id) => new FactKey(FactType.ExtinguisherAt, id);
        public static FactKey Alarm => new FactKey(FactType.AlarmActive, 0);
        public static FactKey Noise(int room) => new FactKey(FactType.NoiseInRoom, room);
        public static FactKey Ordered(EntityId e) => new FactKey(FactType.OrderedToFollow, e.Value);

        public bool Equals(FactKey o) => Type == o.Type && Subject == o.Subject;
        public override bool Equals(object obj) => obj is FactKey k && Equals(k);
        public override int GetHashCode() => ((int)Type * 397) ^ Subject;
        public override string ToString() => Type + "(" + Subject + ")";
    }

    public enum BeliefSourceKind { Prior, Perceived, Told, Inferred }

    public struct BeliefSource
    {
        public BeliefSourceKind Kind;
        public EntityId Teller;
        public int Hops;

        public static BeliefSource Perceived => new BeliefSource { Kind = BeliefSourceKind.Perceived };
        public static BeliefSource Prior => new BeliefSource { Kind = BeliefSourceKind.Prior };
        public static BeliefSource Inferred => new BeliefSource { Kind = BeliefSourceKind.Inferred };
        public static BeliefSource Told(EntityId teller, int hops) => new BeliefSource { Kind = BeliefSourceKind.Told, Teller = teller, Hops = hops };

        public override string ToString() => Kind == BeliefSourceKind.Told ? "told by " + Teller + " (" + Hops + " hop" + (Hops > 1 ? "s)" : ")") : Kind.ToString().ToLowerInvariant();
    }

    public enum BeliefStatus { Tentative, Believed, Confirmed, Contested, Stale }

    public enum RevisionKind { Formed, Strengthened, Weakened, Contradicted, Revised, Rejected }

    public struct BeliefRevision
    {
        public float Time;
        public RevisionKind Kind;
        public float Value;
        public float Confidence;
        public BeliefSource Source;
    }

    /// <summary>
    /// A belief is not a value but a small body of evidence: current claim, confidence, supporting evidence count,
    /// contradictions, a competing claim when contested, and a bounded revision history.
    /// </summary>
    public sealed class Belief
    {
        public FactKey Key;
        public float Value;
        public float Confidence;
        public float Time;
        public Vector3 Position;
        public BeliefSource Source;
        public bool Verified;
        public int Evidence = 1;
        public int Contradictions;
        public bool HasAlternative;
        public float AltValue;
        public float AltConfidence;
        public BeliefSource AltSource;
        public readonly List<BeliefRevision> History = new List<BeliefRevision>();
        public const int HistoryLimit = 6;

        public float EffectiveConfidence(float now)
        {
            float halfLife = HalfLife(Key.Type);
            if (float.IsInfinity(halfLife)) return Confidence;
            return Confidence * Mathf.Exp(-0.693f * Mathf.Max(0f, now - Time) / halfLife);
        }

        public BeliefStatus StatusAt(float now)
        {
            float c = EffectiveConfidence(now);
            if (c < 0.1f) return BeliefStatus.Stale;
            if (HasAlternative && AltConfidence > 0.15f) return BeliefStatus.Contested;
            if (Source.Kind == BeliefSourceKind.Perceived || Evidence >= 2) return c >= 0.6f ? BeliefStatus.Confirmed : BeliefStatus.Believed;
            return c >= 0.5f ? BeliefStatus.Believed : BeliefStatus.Tentative;
        }

        /// <summary>0 = certain, 1 = no idea / two equally strong claims.</summary>
        public float Doubt(float now)
        {
            float c = EffectiveConfidence(now);
            float d = 1f - c;
            if (HasAlternative) d = Mathf.Max(d, 1f - Mathf.Abs(c - AltConfidence));
            return Mathf.Clamp01(d);
        }

        public void Record(RevisionKind kind, float time, float value, float confidence, BeliefSource source)
        {
            History.Add(new BeliefRevision { Kind = kind, Time = time, Value = value, Confidence = confidence, Source = source });
            if (History.Count > HistoryLimit) History.RemoveAt(0);
        }

        /// <summary>What gets shared in a message: the claim and its support, never the holder's private history.</summary>
        public Belief Snapshot()
            => new Belief { Key = Key, Value = Value, Confidence = Confidence, Time = Time, Position = Position, Source = Source, Verified = Verified, Evidence = Evidence };

        public static float HalfLife(FactType t)
        {
            switch (t)
            {
                case FactType.HazardInRoom: return 60f;
                case FactType.SmokeInRoom: return 45f;
                case FactType.DoorState: return 180f;
                case FactType.ExitKnown: return float.PositiveInfinity;
                case FactType.AgentRoom: return 20f;
                case FactType.AgentInjured: return 90f;
                case FactType.AgentSafe: return 300f;
                case FactType.AgentEvacuating: return 15f;
                case FactType.AgentAware: return 180f;
                case FactType.ExtinguisherAt: return 240f;
                case FactType.AlarmActive: return 600f;
                case FactType.NoiseInRoom: return 20f;
                case FactType.OrderedToFollow: return 10f;
                default: return 60f;
            }
        }

        /// <summary>Discrete facts (door states, yes/no) agree only on equality; continuous ones within a tolerance.</summary>
        public static bool Agrees(FactType t, float a, float b)
        {
            switch (t)
            {
                case FactType.HazardInRoom: return (a > 0.05f) == (b > 0.05f) && Mathf.Abs(a - b) < 0.35f;
                case FactType.SmokeInRoom: return Mathf.Abs(a - b) < 0.25f;
                case FactType.NoiseInRoom: return Mathf.Abs(a - b) < 0.4f;
                default: return Mathf.Abs(a - b) < 0.5f;
            }
        }
    }

    public struct BeliefUpdate
    {
        public bool Accepted;
        public bool Changed;
        public bool IsNew;
        public float OldValue;
        public float OldWeighted;
        public Belief Belief;
        public RevisionKind Revision;
        public bool BecameContested;
        public EntityId ContradictedTeller;
        public EntityId ConfirmedTeller;
    }

    /// <summary>
    /// The agent's model of the world and the ONLY world data decision code may read (principle P1).
    /// Written exclusively by perception, memory consolidation and communication.
    /// </summary>
    public sealed class BeliefStore
    {
        private readonly Dictionary<FactKey, Belief> _facts = new Dictionary<FactKey, Belief>();
        // Per-type index: queries like "every agent I know about" must not scan the whole store (O(n^2) with crowds).
        private readonly List<Belief>[] _byType = CreateIndex();

        private static List<Belief>[] CreateIndex()
        {
            var values = Enum.GetValues(typeof(FactType));
            var index = new List<Belief>[values.Length];
            for (int i = 0; i < index.Length; i++) index[i] = new List<Belief>();
            return index;
        }

        /// <summary>All beliefs of one type (live list, do not modify).</summary>
        public IReadOnlyList<Belief> OfType(FactType type) => _byType[(int)type];

        public int Count => _facts.Count;
        public IEnumerable<Belief> All => _facts.Values;

        /// <summary>
        /// Belief revision. Agreeing evidence strengthens (noisy-OR, independent sources count more). Perception
        /// that disagrees revises the belief and records the contradiction. Hearsay that disagrees with a stronger
        /// belief does not overwrite it: the belief becomes contested, weakened, and keeps the competing claim.
        /// </summary>
        public BeliefUpdate Observe(FactKey key, float value, float confidence, float now, BeliefSource source, Vector3 position = default(Vector3))
        {
            var result = new BeliefUpdate();
            confidence = Mathf.Clamp01(confidence);
            Belief existing;
            if (!_facts.TryGetValue(key, out existing))
            {
                var b = new Belief { Key = key, Value = value, Confidence = confidence, Time = now, Position = position, Source = source };
                b.Record(RevisionKind.Formed, now, value, confidence, source);
                _facts[key] = b;
                _byType[(int)key.Type].Add(b);
                result.Accepted = result.Changed = result.IsNew = true;
                result.Belief = b;
                result.Revision = RevisionKind.Formed;
                return result;
            }

            float oldEff = existing.EffectiveConfidence(now);
            result.OldValue = existing.Value;
            result.OldWeighted = existing.Value * oldEff;
            result.Belief = existing;
            bool perceived = source.Kind == BeliefSourceKind.Perceived;
            bool agrees = Belief.Agrees(key.Type, value, existing.Value);

            // Verification of hearsay by direct perception feeds relationships and source reliability.
            if (perceived && IsVerifiable(key.Type))
            {
                if (existing.Source.Kind == BeliefSourceKind.Told && existing.Source.Teller.IsValid && !existing.Verified && now - existing.Time < 90f)
                {
                    if (!agrees) result.ContradictedTeller = existing.Source.Teller;
                    else result.ConfirmedTeller = existing.Source.Teller;
                }
                else if (existing.HasAlternative && existing.AltSource.Kind == BeliefSourceKind.Told && existing.AltSource.Teller.IsValid)
                {
                    if (Belief.Agrees(key.Type, value, existing.AltValue)) result.ConfirmedTeller = existing.AltSource.Teller;
                    else result.ContradictedTeller = existing.AltSource.Teller;
                }
            }

            if (agrees)
            {
                bool independent = source.Kind != existing.Source.Kind || source.Teller != existing.Source.Teller;
                float combined = 1f - (1f - oldEff) * (1f - confidence * (independent ? 0.8f : 0.4f));
                if (perceived) combined = Mathf.Max(combined, confidence);
                bool strengthened = combined > oldEff + 0.01f;
                existing.Value = perceived ? value : Mathf.Lerp(existing.Value, value, confidence / (confidence + oldEff + 1e-4f));
                existing.Confidence = Mathf.Clamp01(combined);
                existing.Time = now;
                if (independent) existing.Evidence++;
                if (perceived)
                {
                    existing.Source = source;
                    existing.HasAlternative = false;
                    existing.Verified = existing.Verified || result.ConfirmedTeller.IsValid;
                }
                if (position != default(Vector3)) existing.Position = position;
                if (strengthened) existing.Record(RevisionKind.Strengthened, now, value, existing.Confidence, source);
                result.Accepted = true;
                result.Revision = RevisionKind.Strengthened;
                result.Changed = Mathf.Abs(result.OldValue - value) > 0.05f || oldEff < 0.3f;
                return result;
            }

            existing.Contradictions++;
            bool strongOwn = existing.Source.Kind == BeliefSourceKind.Perceived && oldEff > 0.3f;
            if (perceived || (!strongOwn && confidence > oldEff * 1.3f))
            {
                // Revise: the new claim wins. Old hearsay is kept as the competing claim; old perception is simply outdated.
                bool keepOld = !perceived && oldEff > 0.25f;
                existing.HasAlternative = keepOld;
                existing.AltValue = existing.Value;
                existing.AltConfidence = keepOld ? oldEff * 0.7f : 0f;
                existing.AltSource = existing.Source;
                existing.Value = value;
                existing.Confidence = keepOld ? confidence * (1f - 0.3f * oldEff) : confidence;
                existing.Time = now;
                existing.Source = source;
                existing.Evidence = 1;
                existing.Verified = perceived && (result.ContradictedTeller.IsValid || result.ConfirmedTeller.IsValid || existing.Verified);
                if (position != default(Vector3)) existing.Position = position;
                existing.Record(RevisionKind.Revised, now, value, existing.Confidence, source);
                result.Accepted = true;
                result.Changed = true;
                result.Revision = RevisionKind.Revised;
                result.BecameContested = keepOld;
                return result;
            }

            // Weaker disagreeing claim: do not overwrite, but doubt. Keep the strongest competing claim.
            existing.Confidence = oldEff * (1f - (strongOwn ? 0.25f : 0.45f) * confidence);
            existing.Time = now;
            if (!existing.HasAlternative || confidence >= existing.AltConfidence)
            {
                existing.HasAlternative = true;
                existing.AltValue = value;
                existing.AltConfidence = confidence;
                existing.AltSource = source;
            }
            existing.Record(RevisionKind.Contradicted, now, value, confidence, source);
            result.Revision = RevisionKind.Contradicted;
            result.BecameContested = true;
            return result;
        }

        private static bool IsVerifiable(FactType t)
            => t == FactType.DoorState || t == FactType.HazardInRoom || t == FactType.ExitKnown || t == FactType.AgentInjured;

        public bool TryGet(FactKey key, out Belief belief) => _facts.TryGetValue(key, out belief);

        public float Confidence(FactKey key, float now)
        {
            Belief b;
            return _facts.TryGetValue(key, out b) ? b.EffectiveConfidence(now) : 0f;
        }

        /// <summary>Value if held with at least minConfidence, otherwise fallback.</summary>
        public float Value(FactKey key, float now, float fallback = 0f, float minConfidence = 0.15f)
        {
            Belief b;
            if (!_facts.TryGetValue(key, out b) || b.EffectiveConfidence(now) < minConfidence) return fallback;
            return b.Value;
        }

        /// <summary>Value x effective confidence. The standard way considerations read uncertain facts.</summary>
        public float Weighted(FactKey key, float now)
        {
            Belief b;
            return _facts.TryGetValue(key, out b) ? b.Value * b.EffectiveConfidence(now) : 0f;
        }

        public bool TryGetPosition(FactKey key, float now, float minConfidence, out Vector3 position)
        {
            Belief b;
            if (_facts.TryGetValue(key, out b) && b.EffectiveConfidence(now) >= minConfidence)
            {
                position = b.Position;
                return true;
            }
            position = default(Vector3);
            return false;
        }

        public void Collect(FactType type, List<Belief> into) { into.AddRange(_byType[(int)type]); }

        public void Forget(FactKey key)
        {
            Belief b;
            if (!_facts.TryGetValue(key, out b)) return;
            _facts.Remove(key);
            _byType[(int)key.Type].Remove(b);
        }
    }
}
