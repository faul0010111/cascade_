namespace Cascade.World
{
    /// <summary>Physical harm from hazards per second of exposure. World truth applied to bodies, not a belief.</summary>
    public static class HazardDamage
    {
        public static float SmokePerSecond = 0.012f;
        public static float HeatPerSecond = 0.25f;
        public static float FirePerSecond = 0.45f;

        public static float Compute(HazardSample s, float dt)
        {
            float rate = s.Smoke * SmokePerSecond;
            if (s.Heat > 0.5f) rate += (s.Heat - 0.5f) * HeatPerSecond;
            if (s.Fire > 0.3f) rate += s.Fire * FirePerSecond;
            return rate * dt;
        }
    }
}
