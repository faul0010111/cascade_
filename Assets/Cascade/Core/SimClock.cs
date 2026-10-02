namespace Cascade.Core
{
    /// <summary>
    /// Simulation time. Every system reads time from here, never from UnityEngine.Time (principle P7).
    /// Supports pause, time scale and single stepping for the Simulation Lab.
    /// </summary>
    public sealed class SimClock
    {
        public float Now { get; private set; }
        public float TimeScale { get; set; } = 1f;
        public bool Paused { get; set; }
        public float StepSize { get; set; } = 0.1f;
        public long Frame { get; private set; }

        private int _pendingSteps;

        /// <summary>Advances simulation time from a real-time delta. Returns the simulated delta for this frame.</summary>
        public float Advance(float realDelta)
        {
            float dt;
            if (Paused)
            {
                if (_pendingSteps <= 0) return 0f;
                _pendingSteps--;
                dt = StepSize;
            }
            else
            {
                if (realDelta > 0.1f) realDelta = 0.1f; // avoid spiral of death after hitches
                dt = realDelta * TimeScale;
            }
            Now += dt;
            Frame++;
            return dt;
        }

        public void StepOnce() { _pendingSteps++; }

        public void Reset()
        {
            Now = 0f;
            Frame = 0;
            _pendingSteps = 0;
        }
    }
}
