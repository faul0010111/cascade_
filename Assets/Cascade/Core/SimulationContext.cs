namespace Cascade.Core
{
    /// <summary>Shared services of one simulation run. Disposing the context resets the run; no system keeps static mutable state.</summary>
    public interface ISimulationContext
    {
        SimClock Clock { get; }
        EventBus Bus { get; }
        EntityRegistry Entities { get; }
        int Seed { get; }
        Rng CreateRng(string key);
    }

    public sealed class SimulationContext : ISimulationContext
    {
        public SimClock Clock { get; } = new SimClock();
        public EventBus Bus { get; } = new EventBus();
        public EntityRegistry Entities { get; } = new EntityRegistry();
        public int Seed { get; }

        public SimulationContext(int seed) { Seed = seed; }

        public Rng CreateRng(string key) => Rng.Stream(Seed, key);
    }
}
