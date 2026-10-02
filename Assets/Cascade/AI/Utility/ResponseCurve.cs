using System;
using UnityEngine;

namespace Cascade.AI.Utility
{
    public enum CurveType { Linear, Polynomial, Logistic, Step }

    /// <summary>
    /// Maps a normalized input in [0, 1] to a score in [0, 1].
    /// Linear/Polynomial: y = m * (x - c)^k + b. Logistic: y = 1 / (1 + e^(-m * (x - c))) + b. Step: x >= c ? 1 : 0.
    /// </summary>
    [Serializable]
    public struct ResponseCurve
    {
        public CurveType Type;
        public float Slope;     // m
        public float Exponent;  // k
        public float XShift;    // c
        public float YShift;    // b
        public bool Invert;

        public ResponseCurve(CurveType type, float slope = 1f, float exponent = 1f, float xShift = 0f, float yShift = 0f, bool invert = false)
        {
            Type = type; Slope = slope; Exponent = exponent; XShift = xShift; YShift = yShift; Invert = invert;
        }

        public static ResponseCurve Linear => new ResponseCurve(CurveType.Linear);
        public static ResponseCurve InverseLinear => new ResponseCurve(CurveType.Linear, invert: true);
        public static ResponseCurve Quadratic => new ResponseCurve(CurveType.Polynomial, exponent: 2f);
        public static ResponseCurve Sqrt => new ResponseCurve(CurveType.Polynomial, exponent: 0.5f);
        public static ResponseCurve Logistic(float steepness, float midpoint, bool invert = false)
            => new ResponseCurve(CurveType.Logistic, steepness, 1f, midpoint, 0f, invert);
        public static ResponseCurve Step(float threshold) => new ResponseCurve(CurveType.Step, xShift: threshold);
        /// <summary>Linear but never below floor: useful to soften a factor instead of vetoing.</summary>
        public static ResponseCurve Floor(float floor) => new ResponseCurve(CurveType.Linear, 1f - floor, 1f, 0f, floor);
        public static ResponseCurve InverseFloor(float floor) => new ResponseCurve(CurveType.Linear, 1f - floor, 1f, 0f, floor, true);

        public float Evaluate(float x)
        {
            x = Mathf.Clamp01(x);
            if (Invert) x = 1f - x;
            float y;
            switch (Type)
            {
                case CurveType.Polynomial:
                    {
                        float d = Mathf.Max(0f, x - XShift);
                        float s = Slope == 0f ? 1f : Slope;
                        y = s * Mathf.Pow(d, Exponent) + YShift;
                        break;
                    }
                case CurveType.Logistic:
                    y = 1f / (1f + Mathf.Exp(-Slope * (x - XShift))) + YShift;
                    break;
                case CurveType.Step:
                    y = x >= XShift ? 1f : 0f;
                    break;
                default:
                    {
                        float s = Slope == 0f ? 1f : Slope;
                        y = s * (x - XShift) + YShift;
                        break;
                    }
            }
            return Mathf.Clamp01(y);
        }
    }
}
