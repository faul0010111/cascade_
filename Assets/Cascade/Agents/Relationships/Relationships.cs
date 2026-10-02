using System.Collections.Generic;
using Cascade.Core;
using UnityEngine;

namespace Cascade.Agents
{
    public enum RelationshipKind { Stranger, Acquaintance, Colleague, Friend, Family }

    public enum SocialEventKind
    {
        HelpedMe, AbandonedMe, GaveCorrectInfo, GaveWrongInfo, LedMeToSafety, PushedMe, HelpedOther,
        WarnedMe, IgnoredMyRequest, FollowedMe, RefusedMyOrder, LedMeIntoDanger, SavedMyLife
    }

    public struct SocialEventRecord
    {
        public SocialEventKind Kind;
        public float Time;
    }

    public sealed class Relationship
    {
        public EntityId Other;
        public RelationshipKind Kind;
        public float Trust, Fear, Respect, Affection, Dependency, Suspicion;
        /// <summary>Learned accuracy of this person's information (0..1), from verified and falsified claims.</summary>
        public float Reliability = 0.5f;
        public int ClaimsChecked;
        public readonly List<SocialEventRecord> History = new List<SocialEventRecord>();

        public override string ToString()
            => Kind + " T" + Pct(Trust) + " Rel" + Pct(Reliability) + " R" + Pct(Respect) + " A" + Pct(Affection) + " F" + Pct(Fear) + " S" + Pct(Suspicion);

        private static string Pct(float v) => Mathf.RoundToInt(v * 100).ToString();
    }

    /// <summary>
    /// Sparse per-agent relationships. They change ONLY through social events, appraised with personality.
    /// The player is an entity like any other: there is no global reputation score.
    /// </summary>
    public sealed class RelationshipBook
    {
        private readonly Dictionary<EntityId, Relationship> _relations = new Dictionary<EntityId, Relationship>();
        private readonly Personality _personality;
        private const int HistoryLimit = 12;

        public RelationshipBook(Personality personality) { _personality = personality; }

        /// <summary>Learned shift in how much strangers are trusted (set by the learning system).</summary>
        public System.Func<float> TrustBias = () => 0f;

        public int Count => _relations.Count;
        public IEnumerable<Relationship> All => _relations.Values;

        public bool Has(EntityId other) => _relations.ContainsKey(other);

        public Relationship Get(EntityId other)
        {
            Relationship r;
            if (_relations.TryGetValue(other, out r)) return r;
            float trustTrait = _personality[Trait.Trust];
            r = new Relationship
            {
                Other = other,
                Kind = RelationshipKind.Stranger,
                Trust = Mathf.Clamp01(0.25f + 0.35f * trustTrait + TrustBias()),
                Respect = 0.3f,
                Suspicion = 0.3f * (1f - trustTrait)
            };
            _relations[other] = r;
            return r;
        }

        public Relationship Peek(EntityId other)
        {
            Relationship r;
            return _relations.TryGetValue(other, out r) ? r : null;
        }

        public void SetInitial(EntityId other, RelationshipKind kind, float trust, float affection)
        {
            var r = Get(other);
            r.Kind = kind;
            r.Trust = Mathf.Clamp01(trust);
            r.Affection = Mathf.Clamp01(affection);
            r.Respect = kind == RelationshipKind.Colleague ? 0.45f : 0.4f;
            r.Dependency = kind == RelationshipKind.Family ? 0.5f : 0f;
            r.Suspicion = 0f;
            r.Reliability = Mathf.Clamp01(0.3f + 0.6f * trust);
        }

        public void Apply(EntityId other, SocialEventKind kind, float now, float magnitude = 1f)
        {
            var r = Get(other);
            float trustUp = 0.5f + _personality[Trait.Trust];
            float trustDown = 1.5f - _personality[Trait.Trust];
            float m = magnitude;
            switch (kind)
            {
                case SocialEventKind.HelpedMe:
                    r.Trust += .25f * m * trustUp; r.Affection += .15f * m; r.Dependency += .10f * m; r.Respect += .10f * m; r.Suspicion -= .10f * m; break;
                case SocialEventKind.AbandonedMe:
                    r.Trust -= .35f * m * trustDown; r.Affection -= .15f * m; r.Suspicion += .25f * m; break;
                case SocialEventKind.GaveCorrectInfo:
                    r.Trust += .12f * m * trustUp; r.Respect += .08f * m; break;
                case SocialEventKind.GaveWrongInfo:
                    r.Trust -= .20f * m * trustDown; r.Suspicion += .10f * m; break;
                case SocialEventKind.LedMeToSafety:
                    r.Respect += .30f * m; r.Trust += .20f * m * trustUp; r.Affection += .10f * m; break;
                case SocialEventKind.PushedMe:
                    r.Fear += .20f * m; r.Suspicion += .30f * m; r.Affection -= .20f * m; r.Trust -= .15f * m * trustDown; break;
                case SocialEventKind.HelpedOther:
                    r.Respect += .12f * m; r.Trust += .06f * m * trustUp; break;
                case SocialEventKind.WarnedMe:
                    r.Trust += .08f * m * trustUp; r.Respect += .05f * m; break;
                case SocialEventKind.IgnoredMyRequest:
                    r.Trust -= .10f * m * trustDown; r.Affection -= .05f * m; break;
                case SocialEventKind.LedMeIntoDanger:
                    r.Trust -= .30f * m * trustDown * (0.5f + r.Trust); r.Suspicion += .25f * m; r.Respect -= .15f * m; r.Dependency -= .2f * m; break;
                case SocialEventKind.SavedMyLife:
                    r.Trust += .35f * m * trustUp; r.Respect += .35f * m; r.Dependency += .25f * m; r.Affection += .2f * m; r.Suspicion -= .2f * m; break;
                case SocialEventKind.FollowedMe:
                    r.Affection += .05f * m; r.Trust += .03f * m; break;
                case SocialEventKind.RefusedMyOrder:
                    r.Respect -= .05f * m; break;
            }
            r.Trust = Mathf.Clamp01(r.Trust); r.Fear = Mathf.Clamp01(r.Fear); r.Respect = Mathf.Clamp01(r.Respect);
            r.Affection = Mathf.Clamp01(r.Affection); r.Dependency = Mathf.Clamp01(r.Dependency); r.Suspicion = Mathf.Clamp01(r.Suspicion);
            if (r.Kind == RelationshipKind.Stranger) r.Kind = RelationshipKind.Acquaintance;
            r.History.Add(new SocialEventRecord { Kind = kind, Time = now });
            if (r.History.Count > HistoryLimit) r.History.RemoveAt(0);
        }
    }
}
