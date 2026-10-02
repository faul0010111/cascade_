using UnityEngine;

namespace Cascade.Agents
{
    public enum EmotionState { Calm, Nervous, Afraid, Panicked, Confident, Angry, Confused, Relieved }

    /// <summary>How the current emotion modulates the other layers (the "Mode" layer of ARCHITECTURE.md section 4).</summary>
    public struct EmotionModifiers
    {
        public float DecisionIntervalMul;
        public float SelectionTemperature;
        public float FovMul;
        public float PlanningBudgetMul;
        public float SpeedMul;
        public int MaskedGoals; // bit per GoalType

        public bool Masks(GoalType g) => (MaskedGoals & (1 << (int)g)) != 0;

        public static EmotionModifiers For(EmotionState e)
        {
            switch (e)
            {
                case EmotionState.Nervous:   return M(0.85f, 0.10f, 0.95f, 1.0f, 1.05f);
                case EmotionState.Afraid:    return M(0.70f, 0.15f, 0.90f, 1.0f, 1.15f, GoalType.Investigate, GoalType.Routine, GoalType.Verify);
                case EmotionState.Panicked:  return M(0.40f, 0.35f, 0.65f, 0.3f, 1.25f, GoalType.Investigate, GoalType.Routine, GoalType.WarnOther, GoalType.GatherGroup, GoalType.FightFire, GoalType.Verify);
                case EmotionState.Confident: return M(0.90f, 0.05f, 1.00f, 1.2f, 1.05f);
                case EmotionState.Angry:     return M(0.80f, 0.20f, 0.90f, 1.0f, 1.10f);
                case EmotionState.Confused:  return M(1.30f, 0.25f, 0.95f, 0.6f, 0.90f);
                case EmotionState.Relieved:  return M(1.00f, 0.05f, 1.00f, 1.0f, 0.90f);
                default:                     return M(1.00f, 0.05f, 1.00f, 1.0f, 1.00f);
            }
        }

        private static EmotionModifiers M(float interval, float temp, float fov, float budget, float speed, params GoalType[] masked)
        {
            int mask = 0;
            foreach (var g in masked) mask |= 1 << (int)g;
            return new EmotionModifiers
            {
                DecisionIntervalMul = interval, SelectionTemperature = temp, FovMul = fov,
                PlanningBudgetMul = budget, SpeedMul = speed, MaskedGoals = mask
            };
        }
    }

    /// <summary>
    /// Emotion is DERIVED from internal state, with hysteresis so it does not flicker at thresholds.
    /// It is a gameplay abstraction, not a psychological model.
    /// </summary>
    public sealed class EmotionModel
    {
        public EmotionState Current { get; private set; } = EmotionState.Calm;
        public float EnteredAt { get; private set; }
        public EmotionModifiers Modifiers { get; private set; } = EmotionModifiers.For(EmotionState.Calm);

        /// <summary>0..1, raised when beliefs contradict each other (e.g. two sources disagree). Decays over time.</summary>
        public float ConflictLevel { get; private set; }

        private const int HistorySize = 12;         // 12 samples x 0.5 s = 6 s of fear history
        private readonly float[] _fearHistory = new float[HistorySize];
        private int _historyIndex;
        private float _nextSample;

        public void AddConflict(float amount) { ConflictLevel = Mathf.Clamp01(ConflictLevel + amount); }

        /// <summary>Returns true when the discrete state changed.</summary>
        public bool Update(InternalState s, float now, float dt)
        {
            ConflictLevel = Mathf.MoveTowards(ConflictLevel, 0f, 0.05f * dt);
            float fear = s[StateVar.Fear], stress = s[StateVar.Stress], conf = s[StateVar.Confidence];
            float aggr = s[StateVar.Aggression], aware = s[StateVar.Awareness];

            if (now >= _nextSample)
            {
                _fearHistory[_historyIndex] = fear;
                _historyIndex = (_historyIndex + 1) % HistorySize;
                _nextSample = now + 0.5f;
            }
            float maxRecentFear = 0f;
            for (int i = 0; i < HistorySize; i++) maxRecentFear = Mathf.Max(maxRecentFear, _fearHistory[i]);

            var c = Current;
            EmotionState next;
            if (c == EmotionState.Panicked && fear > 0.6f) next = EmotionState.Panicked;
            else if (fear > 0.8f && stress > 0.7f) next = EmotionState.Panicked;
            else if (c == EmotionState.Relieved && now - EnteredAt < 4f && fear < 0.4f) next = EmotionState.Relieved;
            else if (maxRecentFear - fear > 0.3f && fear < 0.35f) next = EmotionState.Relieved;
            else if (aggr > (c == EmotionState.Angry ? 0.5f : 0.65f)) next = EmotionState.Angry;
            else if (aware > 0.4f && ConflictLevel > (c == EmotionState.Confused ? 0.3f : 0.5f) && conf < (c == EmotionState.Confused ? 0.45f : 0.35f)) next = EmotionState.Confused;
            else if (fear > (c == EmotionState.Afraid ? 0.45f : 0.55f)) next = EmotionState.Afraid;
            else if (fear > (c == EmotionState.Nervous ? 0.2f : 0.25f) || stress > (c == EmotionState.Nervous ? 0.3f : 0.35f)) next = EmotionState.Nervous;
            else if (conf > 0.65f && aware > 0.3f) next = EmotionState.Confident;
            else next = EmotionState.Calm;

            if (next == c) return false;
            Current = next;
            EnteredAt = now;
            Modifiers = EmotionModifiers.For(next);
            return true;
        }
    }
}
