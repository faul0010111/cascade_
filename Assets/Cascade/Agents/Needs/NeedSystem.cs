using UnityEngine;

namespace Cascade.Agents
{
    public enum NeedType { Safety, Survival, Social, Information, Rest, Assistance, Escape, ProtectOthers }

    /// <summary>
    /// How much each need matters right now, recomputed from beliefs and internal state. Goals declare which needs they
    /// serve, so context shifts priorities without per-situation rules.
    /// </summary>
    public sealed class NeedSystem
    {
        public const int Count = 8;
        private readonly float[] _values = new float[Count];

        public float this[NeedType n] => _values[(int)n];

        public void Update(AgentBrain b, float now)
        {
            var s = b.State;
            float dangerHere = b.DangerAt(b.CurrentRoom, now);
            float threat = b.KnownThreatLevel(now);
            float aware = s[StateVar.Awareness];

            Set(NeedType.Safety, Mathf.Max(dangerHere, s[StateVar.Fear] * 0.8f));
            Set(NeedType.Survival, Mathf.Max(1f - s[StateVar.Health], dangerHere * 0.7f));
            Set(NeedType.Rest, 1f - s[StateVar.Energy]);
            Set(NeedType.Social, s[StateVar.SocialNeed] + s[StateVar.Stress] * 0.3f * (1f - b.Personality[Trait.Independence]));

            float alarm = b.Beliefs.Weighted(FactKey.Alarm, now);
            float escape = b.AtSafety ? 0f : Mathf.Max(threat, Mathf.Max(alarm * 0.6f, dangerHere)) * (aware > 0.25f ? 1f : 0.4f) + s[StateVar.Fear] * 0.2f;
            Set(NeedType.Escape, escape);

            // Aware that something is wrong but not knowing where or what it is.
            float infoSeeking = 1f + b.Learning.Dispositions.InformationSeeking;
            Set(NeedType.Information, Mathf.Max(aware * (1f - Mathf.Clamp01(threat * 1.5f)), aware * b.Self.Uncertainty * infoSeeking) * (b.AtSafety ? 0.2f : 1f));

            Set(NeedType.Assistance, b.IsInjured ? 1f : s[StateVar.Fear] * (0.3f + 0.5f * b.Self.Uncertainty + 0.3f * (1f - b.Self.PlanConfidence)));
            Set(NeedType.ProtectOthers, b.StrongestProtectiveUrge(now));
        }

        private void Set(NeedType n, float v) { _values[(int)n] = Mathf.Clamp01(v); }
    }
}
