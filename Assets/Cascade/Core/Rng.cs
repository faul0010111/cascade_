using System;

namespace Cascade.Core
{
    /// <summary>
    /// Deterministic SplitMix64 generator. Each system and each agent owns its own stream derived from the
    /// root seed, so adding an agent never changes the random sequence of another (principle P7).
    /// </summary>
    public sealed class Rng
    {
        private ulong _state;

        public Rng(ulong seed) { _state = seed == 0 ? 0x9E3779B97F4A7C15UL : seed; }

        public static Rng Stream(int rootSeed, string key) => new Rng(Hash(rootSeed, key));
        public static Rng Stream(int rootSeed, int key) => new Rng(Hash(rootSeed, "id:" + key));

        public ulong NextULong()
        {
            ulong z = (_state += 0x9E3779B97F4A7C15UL);
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }

        /// <summary>Uniform float in [0, 1).</summary>
        public float Value() => (NextULong() >> 40) * (1f / 16777216f);

        public float Range(float min, float max) => min + (max - min) * Value();

        /// <summary>Uniform int in [min, maxExclusive).</summary>
        public int Range(int min, int maxExclusive)
        {
            if (maxExclusive <= min) return min;
            return min + (int)(NextULong() % (ulong)(maxExclusive - min));
        }

        public bool Chance(float probability) => Value() < probability;

        public float Gaussian(float mean, float stdDev)
        {
            float u1 = 1f - Value();
            float u2 = Value();
            float n = (float)(Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Sin(2.0 * Math.PI * u2));
            return mean + stdDev * n;
        }

        private static ulong Hash(int seed, string key)
        {
            ulong h = 14695981039346656037UL ^ (ulong)(uint)seed;
            for (int i = 0; i < key.Length; i++)
            {
                h ^= key[i];
                h *= 1099511628211UL;
            }
            return h;
        }
    }
}
