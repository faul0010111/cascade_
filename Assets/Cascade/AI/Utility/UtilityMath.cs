using System.Collections.Generic;
using Cascade.Core;
using UnityEngine;

namespace Cascade.AI.Utility
{
    public static class UtilityMath
    {
        /// <summary>
        /// Compensation factor (Dave Mark, "Building a Better Centaur"): multiplying many scores in [0,1] punishes
        /// options with more considerations. Each score is raised back toward 1 proportionally to the count.
        /// </summary>
        public static float Compensate(float score, int considerationCount)
        {
            if (considerationCount <= 1) return score;
            float modification = 1f - 1f / considerationCount;
            float makeUp = (1f - score) * modification;
            return score + makeUp * score;
        }

        /// <summary>Product of compensated scores. Any zero vetoes the option.</summary>
        public static float Combine(IList<float> scores)
        {
            float result = 1f;
            int n = scores.Count;
            for (int i = 0; i < n; i++)
            {
                if (scores[i] <= 0f) return 0f;
                result *= Compensate(scores[i], n);
            }
            return result;
        }
    }

    public static class UtilitySelector
    {
        /// <summary>
        /// Chooses among options whose score is within <paramref name="margin"/> (fraction) of the best, weighting by a
        /// softmax with the given temperature. Temperature ~0 means argmax. Returns -1 if every score is zero.
        /// </summary>
        public static int Select(IList<float> scores, float temperature, float margin, Rng rng)
        {
            int best = -1;
            float bestScore = 0f;
            for (int i = 0; i < scores.Count; i++)
                if (scores[i] > bestScore) { bestScore = scores[i]; best = i; }
            if (best < 0 || temperature <= 0.001f || rng == null) return best;

            float threshold = bestScore * (1f - margin);
            float total = 0f;
            var weights = new float[scores.Count];
            for (int i = 0; i < scores.Count; i++)
            {
                if (scores[i] < threshold || scores[i] <= 0f) continue;
                weights[i] = Mathf.Exp((scores[i] - bestScore) / temperature);
                total += weights[i];
            }
            float pick = rng.Value() * total;
            for (int i = 0; i < weights.Length; i++)
            {
                if (weights[i] <= 0f) continue;
                pick -= weights[i];
                if (pick <= 0f) return i;
            }
            return best;
        }
    }
}
