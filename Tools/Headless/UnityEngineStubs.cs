// Minimal stand-ins for UnityEngine math types so the pure layers build and test with plain .NET (no Unity).
using System;
namespace UnityEngine
{
    public struct Vector3
    {
        public float x, y, z;
        public Vector3(float x, float y, float z) { this.x = x; this.y = y; this.z = z; }
        public Vector3(float x, float y) { this.x = x; this.y = y; this.z = 0; }
        public static Vector3 zero => new Vector3(0, 0, 0);
        public static Vector3 one => new Vector3(1, 1, 1);
        public static Vector3 up => new Vector3(0, 1, 0);
        public static Vector3 forward => new Vector3(0, 0, 1);
        public static Vector3 right => new Vector3(1, 0, 0);
        public void Normalize() { this = normalized; }
        public float magnitude => (float)Math.Sqrt(x * x + y * y + z * z);
        public float sqrMagnitude => x * x + y * y + z * z;
        public Vector3 normalized { get { var m = magnitude; return m > 1e-5f ? this / m : zero; } }
        public static Vector3 operator +(Vector3 a, Vector3 b) => new Vector3(a.x + b.x, a.y + b.y, a.z + b.z);
        public static Vector3 operator -(Vector3 a, Vector3 b) => new Vector3(a.x - b.x, a.y - b.y, a.z - b.z);
        public static Vector3 operator -(Vector3 a) => new Vector3(-a.x, -a.y, -a.z);
        public static Vector3 operator *(Vector3 a, float d) => new Vector3(a.x * d, a.y * d, a.z * d);
        public static Vector3 operator *(float d, Vector3 a) => new Vector3(a.x * d, a.y * d, a.z * d);
        public static Vector3 operator /(Vector3 a, float d) => new Vector3(a.x / d, a.y / d, a.z / d);
        public static bool operator ==(Vector3 a, Vector3 b) => (a - b).sqrMagnitude < 1e-10f;
        public static bool operator !=(Vector3 a, Vector3 b) => !(a == b);
        public override bool Equals(object o) => o is Vector3 v && v == this;
        public override int GetHashCode() => x.GetHashCode() ^ y.GetHashCode() ^ z.GetHashCode();
        public static float Distance(Vector3 a, Vector3 b) => (a - b).magnitude;
        public static float Dot(Vector3 a, Vector3 b) => a.x * b.x + a.y * b.y + a.z * b.z;
        public static float Angle(Vector3 a, Vector3 b) { float d = (float)Math.Sqrt(a.sqrMagnitude * b.sqrMagnitude); if (d < 1e-15f) return 0; float dot = Mathf.Clamp(Dot(a, b) / d, -1f, 1f); return (float)Math.Acos(dot) * Mathf.Rad2Deg; }
        public static Vector3 Lerp(Vector3 a, Vector3 b, float t) { t = Mathf.Clamp01(t); return a + (b - a) * t; }
        public static Vector3 MoveTowards(Vector3 c, Vector3 t, float d) { var v = t - c; var m = v.magnitude; if (m <= d || m == 0) return t; return c + v / m * d; }
        public override string ToString() => $"({x:F1}, {y:F1}, {z:F1})";
    }
    public static class Mathf
    {
        public const float Deg2Rad = (float)(Math.PI / 180.0), Rad2Deg = (float)(180.0 / Math.PI), Infinity = float.PositiveInfinity, PI = (float)Math.PI;
        public static float Clamp(float v, float a, float b) => v < a ? a : v > b ? b : v;
        public static int Clamp(int v, int a, int b) => v < a ? a : v > b ? b : v;
        public static float Clamp01(float v) => Clamp(v, 0f, 1f);
        public static float Min(float a, float b) => a < b ? a : b;
        public static float Max(float a, float b) => a > b ? a : b;
        public static int Min(int a, int b) => a < b ? a : b;
        public static int Max(int a, int b) => a > b ? a : b;
        public static float Abs(float v) => Math.Abs(v);
        public static float Exp(float v) => (float)Math.Exp(v);
        public static float Log(float v) => (float)Math.Log(v);
        public static float Pow(float a, float b) => (float)Math.Pow(a, b);
        public static float Sqrt(float v) => (float)Math.Sqrt(v);
        public static float Sin(float v) => (float)Math.Sin(v);
        public static float Cos(float v) => (float)Math.Cos(v);
        public static float Atan2(float y, float x) => (float)Math.Atan2(y, x);
        public static float Sign(float v) => v >= 0 ? 1f : -1f;
        public static int FloorToInt(float v) => (int)Math.Floor(v);
        public static int RoundToInt(float v) => (int)Math.Round(v);
        public static int CeilToInt(float v) => (int)Math.Ceiling(v);
        public static float Lerp(float a, float b, float t) => a + (b - a) * Clamp01(t);
        public static float InverseLerp(float a, float b, float v) => a == b ? 0 : Clamp01((v - a) / (b - a));
        public static float MoveTowards(float c, float t, float d) => Math.Abs(t - c) <= d ? t : c + Math.Sign(t - c) * d;
        public static bool Approximately(float a, float b) => Math.Abs(a - b) < 1e-5f;
    }
}
namespace UnityEngine
{
    public class TooltipAttribute : System.Attribute { public TooltipAttribute(string s) { } }
    public class HeaderAttribute : System.Attribute { public HeaderAttribute(string s) { } }
    public class RangeAttribute : System.Attribute { public RangeAttribute(float a, float b) { } }
    public class SerializeField : System.Attribute { }
    public class TextAreaAttribute : System.Attribute { public TextAreaAttribute() { } public TextAreaAttribute(int a, int b) { } }
}
