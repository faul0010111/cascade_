using System.Collections.Generic;
using Cascade.Agents;
using Cascade.Core;
using Cascade.Simulation;
using Cascade.Simulation.Headless;
using Cascade.World;
using UnityEngine;

namespace Cascade.Tests
{
    /// <summary>Helpers that build small deterministic worlds without Unity scenes.</summary>
    public static class Fixtures
    {
        public static SimulationWorld World(int seed = 1)
        {
            var graph = BuildingLayouts.CreateOfficeFloor();
            var world = new SimulationWorld(seed, graph, new RoomGraphLineOfSight(graph)) { LodEnabled = false, FrameBudgetMs = 1000f };
            BuildingLayouts.AddDefaultInteractables(world.Building);
            return world;
        }

        public static Personality Traits(float courage = .5f, float fear = .5f, float lead = .5f, float empathy = .5f, float curiosity = .5f,
                                         float aggression = .5f, float trust = .5f, float risk = .5f, float patience = .5f, float independence = .5f)
        {
            var p = new Personality();
            p[Trait.Courage] = courage; p[Trait.Fearfulness] = fear; p[Trait.Leadership] = lead; p[Trait.Empathy] = empathy;
            p[Trait.Curiosity] = curiosity; p[Trait.Aggression] = aggression; p[Trait.Trust] = trust; p[Trait.RiskTolerance] = risk;
            p[Trait.Patience] = patience; p[Trait.Independence] = independence;
            return p;
        }

        public static AgentBrain Spawn(SimulationWorld world, string name, Personality p, string room, Vector3 offset = default(Vector3),
                                       bool knowsExits = true, List<HeadlessBody> bodies = null)
        {
            var graph = world.Building.Graph;
            var r = graph.Rooms[graph.FindRoom(room)];
            var body = new HeadlessBody(r.Clamp(r.Center + offset, 0.8f));
            if (bodies != null) bodies.Add(body);
            var id = world.Context.Entities.Register(EntityKind.Npc, name);
            var brain = new AgentBrain(id, new AgentProfile { Name = name, Personality = p, KnowsAllExits = knowsExits }, world.Services, body);
            AgentFactory.ApplyPriors(brain, graph, ScenarioBuilder.FindMainExit(graph, "Front Yard"));
            world.Register(brain, body, false);
            return brain;
        }

        public static void Run(SimulationWorld world, List<HeadlessBody> bodies, float seconds, float dt = 0.1f)
        {
            float end = world.Now + seconds;
            while (world.Now < end)
            {
                world.Step(dt);
                foreach (var b in bodies) b.Step(dt);
            }
        }
    }
}
