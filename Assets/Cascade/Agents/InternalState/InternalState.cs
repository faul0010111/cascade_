using System.Collections.Generic;
using UnityEngine;

namespace Cascade.Agents
{
    public enum StateVar { Stress, Fear, Energy, Health, Confidence, Awareness, Curiosity, Aggression, SocialNeed }

    /// <summary>Continuous internal variables in [0, 1] that drift back toward personality-dependent baselines.</summary>
    public sealed class InternalState
    {
        public const int Count = 9;
        private readonly float[] _values = new float[Count];
        private readonly float[] _baseline = new float[Count];
        private readonly float[] _decay = new float[Count];

        public InternalState(Personality p)
        {
            Set(StateVar.Stress, 0.10f, 0.02f * (0.5f + p[Trait.Patience]));
            Set(StateVar.Fear, 0.05f + 0.10f * p[Trait.Fearfulness], 0.015f * (1.2f - p[Trait.Fearfulness]));
            Set(StateVar.Energy, 1f, 0.01f);
            Set(StateVar.Health, 1f, 0f);
            Set(StateVar.Confidence, 0.3f + 0.4f * p[Trait.Courage], 0.02f);
            Set(StateVar.Awareness, 0f, 0.004f);
            Set(StateVar.Curiosity, p[Trait.Curiosity], 0.02f);
            Set(StateVar.Aggression, 0.1f + 0.3f * p[Trait.Aggression], 0.03f);
            Set(StateVar.SocialNeed, 0.2f + 0.3f * (1f - p[Trait.Independence]), 0.02f);
            for (int i = 0; i < Count; i++) _values[i] = _baseline[i];
        }

        private void Set(StateVar v, float baseline, float decay)
        {
            _baseline[(int)v] = baseline;
            _decay[(int)v] = decay;
        }

        private readonly float[] _offset = new float[Count];

        /// <summary>Learned, bounded shift of a baseline (personality stays; experience moves the resting point a little).</summary>
        public void SetBaselineOffset(StateVar v, float offset) { _offset[(int)v] = offset; }

        public float this[StateVar v] => _values[(int)v];
        public float Baseline(StateVar v) => Mathf.Clamp01(_baseline[(int)v] + _offset[(int)v]);

        public void Add(StateVar v, float delta) { _values[(int)v] = Mathf.Clamp01(_values[(int)v] + delta); }
        public void SetValue(StateVar v, float value) { _values[(int)v] = Mathf.Clamp01(value); }

        public void Tick(float dt)
        {
            for (int i = 0; i < Count; i++)
                _values[i] = Mathf.MoveTowards(_values[i], Mathf.Clamp01(_baseline[i] + _offset[i]), _decay[i] * dt);
        }
    }

    public enum AppraisalEvent
    {
        HeardAlarm, SawFireFar, SawFireClose, InSmoke, NearHeat, RouteBlocked, SawInjured, WasHelped, HelpedSomeone,
        WasAbandoned, ReceivedWarning, OrderedByOther, Injured, ReachedSafety, PlanFailed, TargetFound, TargetLost,
        JoinedGroup, Pushed, InfoProvedWrong
    }

    /// <summary>Data table converting events into internal-state deltas, scaled by personality sensitivities.</summary>
    public static class Appraisal
    {
        private struct Delta
        {
            public StateVar Var;
            public float Amount;
            public Delta(StateVar v, float a) { Var = v; Amount = a; }
        }

        private static readonly Dictionary<AppraisalEvent, Delta[]> Table = new Dictionary<AppraisalEvent, Delta[]>
        {
            { AppraisalEvent.HeardAlarm,      new[] { D(StateVar.Awareness, .50f), D(StateVar.Stress, .12f), D(StateVar.Fear, .08f) } },
            { AppraisalEvent.SawFireFar,      new[] { D(StateVar.Awareness, .40f), D(StateVar.Fear, .20f), D(StateVar.Stress, .20f) } },
            { AppraisalEvent.SawFireClose,    new[] { D(StateVar.Awareness, .50f), D(StateVar.Fear, .35f), D(StateVar.Stress, .30f), D(StateVar.Confidence, -.10f) } },
            { AppraisalEvent.InSmoke,         new[] { D(StateVar.Fear, .08f), D(StateVar.Stress, .10f), D(StateVar.Awareness, .20f) } },
            { AppraisalEvent.NearHeat,        new[] { D(StateVar.Fear, .15f), D(StateVar.Stress, .10f) } },
            { AppraisalEvent.RouteBlocked,    new[] { D(StateVar.Stress, .20f), D(StateVar.Fear, .12f), D(StateVar.Confidence, -.15f) } },
            { AppraisalEvent.SawInjured,      new[] { D(StateVar.Stress, .10f), D(StateVar.SocialNeed, .10f), D(StateVar.Awareness, .20f) } },
            { AppraisalEvent.WasHelped,       new[] { D(StateVar.Fear, -.15f), D(StateVar.Stress, -.10f), D(StateVar.Confidence, .10f), D(StateVar.SocialNeed, -.20f) } },
            { AppraisalEvent.HelpedSomeone,   new[] { D(StateVar.Confidence, .15f), D(StateVar.Stress, -.05f) } },
            { AppraisalEvent.WasAbandoned,    new[] { D(StateVar.Fear, .15f), D(StateVar.Aggression, .15f), D(StateVar.Confidence, -.10f) } },
            { AppraisalEvent.ReceivedWarning, new[] { D(StateVar.Awareness, .40f), D(StateVar.Fear, .10f), D(StateVar.Stress, .08f) } },
            { AppraisalEvent.OrderedByOther,  new[] { D(StateVar.Awareness, .20f), D(StateVar.Stress, -.03f) } },
            { AppraisalEvent.Injured,         new[] { D(StateVar.Fear, .30f), D(StateVar.Stress, .30f), D(StateVar.Energy, -.20f) } },
            { AppraisalEvent.ReachedSafety,   new[] { D(StateVar.Fear, -.45f), D(StateVar.Stress, -.40f), D(StateVar.Confidence, .20f) } },
            { AppraisalEvent.PlanFailed,      new[] { D(StateVar.Stress, .12f), D(StateVar.Confidence, -.10f) } },
            { AppraisalEvent.TargetFound,     new[] { D(StateVar.Stress, -.10f), D(StateVar.Fear, -.10f) } },
            { AppraisalEvent.TargetLost,      new[] { D(StateVar.Stress, .10f) } },
            { AppraisalEvent.JoinedGroup,     new[] { D(StateVar.Fear, -.10f), D(StateVar.SocialNeed, -.20f), D(StateVar.Confidence, .05f) } },
            { AppraisalEvent.Pushed,          new[] { D(StateVar.Aggression, .20f), D(StateVar.Fear, .10f), D(StateVar.Stress, .10f) } },
            { AppraisalEvent.InfoProvedWrong, new[] { D(StateVar.Stress, .05f), D(StateVar.Aggression, .05f) } },
        };

        private static Delta D(StateVar v, float a) => new Delta(v, a);

        public static void Apply(AppraisalEvent evt, float intensity, InternalState state, Personality p)
        {
            Delta[] deltas;
            if (!Table.TryGetValue(evt, out deltas)) return;
            foreach (var d in deltas)
                state.Add(d.Var, d.Amount * intensity * Sensitivity(d.Var, d.Amount, p));
        }

        public static float Sensitivity(StateVar v, float amount, Personality p)
        {
            switch (v)
            {
                case StateVar.Fear:
                    return amount > 0 ? (0.4f + p[Trait.Fearfulness]) * (1.3f - 0.6f * p[Trait.Courage]) : 1f;
                case StateVar.Stress:
                    return amount > 0 ? 1.4f - 0.8f * p[Trait.Patience] : 1f;
                case StateVar.Confidence:
                    return amount > 0 ? 0.7f + 0.6f * p[Trait.Courage] : 1.3f - 0.6f * p[Trait.Courage];
                case StateVar.Aggression:
                    return amount > 0 ? 0.3f + 1.4f * p[Trait.Aggression] : 1f;
                case StateVar.SocialNeed:
                    return amount > 0 ? 1.3f - p[Trait.Independence] : 1f;
                default:
                    return 1f;
            }
        }
    }
}
