namespace Cascade.Agents
{
    /// <summary>
    /// Switches for cognitive mechanisms, used for ablation experiments (Simulation Lab, tests) and for tracing
    /// regressions to a mechanism. Per simulation run (not static), so parallel batch runs do not interfere.
    /// </summary>
    public sealed class CognitionSettings
    {
        public bool Learning = true;            // strategy learning from outcomes (direct)
        public bool SocialLearning = true;      // observed and reported experiences
        public bool LearnedRouteCosts = true;   // learned exit preferences affect routing
        public bool InformationSeeking = true;  // Verify / AskForInfo goals
        public bool PlanDoubt = true;           // abandon plans whose expected success collapses
        public float LearnedExitWeight = 40f;

        public CognitionSettings Clone() => (CognitionSettings)MemberwiseClone();
    }
}
