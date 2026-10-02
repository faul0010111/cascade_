using System.Collections.Generic;
using Cascade.Agents;
using Cascade.Simulation;
using Cascade.Simulation.Headless;
using Cascade.World;
using NUnit.Framework;
using UnityEngine;

namespace Cascade.Tests
{
    /// <summary>Behavior-level tests: whole agents in a headless world. These check tendencies, not scripts.</summary>
    public class BehaviorTests
    {
        [Test]
        public void Communication_TransfersOnlyBeliefs_NeverTruth()
        {
            var world = Fixtures.World();
            var bodies = new List<HeadlessBody>();
            var a = Fixtures.Spawn(world, "A", Fixtures.Traits(), "Office A", bodies: bodies);
            var b = Fixtures.Spawn(world, "B", Fixtures.Traits(), "Office A", new Vector3(1f, 0f, 0f), bodies: bodies);
            world.Building.Hazards.Ignite(new Vector3(36f, 0f, 20f), 0.8f); // Kitchen: nobody can see it
            Fixtures.Run(world, bodies, 1f);
            var m = a.Comms.SendRelevant(MessageKind.Warn, b.Id, 8f);
            foreach (var p in m.Payload) Assert.IsFalse(p.Key.Type == FactType.HazardInRoom && p.Value > 0f, "A's message cannot contain a fire A never saw");
            Fixtures.Run(world, bodies, 0.5f);
            Assert.Less(b.Beliefs.Weighted(FactKey.Hazard(world.Building.Graph.FindRoom("Kitchen")), world.Now), 0.05f,
                "B cannot learn about a fire that A never saw");
        }

        [Test]
        public void Warning_SpreadsKnowledgeAndChangesBehavior()
        {
            var world = Fixtures.World();
            var bodies = new List<HeadlessBody>();
            int kitchen = world.Building.Graph.FindRoom("Kitchen");
            world.Building.Hazards.Ignite(new Vector3(36f, 0f, 20f), 0.7f);
            // The witness stands at the kitchen door and can see the fire; the listener is in Office B and cannot.
            var witness = Fixtures.Spawn(world, "Witness", Fixtures.Traits(), "Corridor", new Vector3(15f, 0f, 1f), bodies: bodies);
            var listener = Fixtures.Spawn(world, "Listener", Fixtures.Traits(trust: .8f), "Office B", bodies: bodies);
            Fixtures.Run(world, bodies, 0.5f);
            Assert.Greater(witness.Beliefs.Weighted(FactKey.Hazard(kitchen), world.Now), 0.3f, "witness saw the fire");
            Assert.Less(listener.Beliefs.Weighted(FactKey.Hazard(kitchen), world.Now), 0.05f, "listener did not");
            Assert.AreEqual(GoalType.Routine, listener.Goals.Current.Type);

            witness.Comms.SendRelevant(MessageKind.Warn, listener.Id, 30f);
            Fixtures.Run(world, bodies, 1.5f);
            Assert.Greater(listener.Beliefs.Weighted(FactKey.Hazard(kitchen), world.Now), 0.1f, "listener learned about the fire by hearsay");
            Assert.AreEqual(BeliefSourceKind.Told, GetSource(listener, FactKey.Hazard(kitchen)));
            Assert.AreNotEqual(GoalType.Routine, listener.Goals.Current.Type, "and acts on it");
        }

        private static BeliefSourceKind GetSource(AgentBrain b, FactKey k)
        {
            Belief belief;
            return b.Beliefs.TryGet(k, out belief) ? belief.Source.Kind : BeliefSourceKind.Prior;
        }

        [Test]
        public void RememberedDanger_ChangesRouteChoice()
        {
            var world = Fixtures.World();
            var brain = Fixtures.Spawn(world, "R", Fixtures.Traits(risk: .1f, courage: .2f), "Corridor", new Vector3(-10f, 0f, 0f));
            brain.Tick(0.1f, 0.1f, LodTier.Full);
            var before = new List<int>();
            float cost; int target;
            Assert.IsTrue(brain.Routes.FindExitRoute(before, out cost, out target));
            Assert.AreEqual(2, before.Count, "from the west end of the corridor the shortest way out is through the Lobby");
            int viaRoom = brain.FloorPlan.DoorOtherSide(before[0], brain.CurrentRoom);
            brain.Memory.Record(MemoryKind.SawFire, Core.EntityId.None, viaRoom, 0.1f, -1f, 1f);
            brain.Routes.Invalidate();
            var after = new List<int>();
            Assert.IsTrue(brain.Routes.FindExitRoute(after, out cost, out target));
            Assert.AreNotEqual(before[0], after[0], "the room remembered as dangerous is avoided");
        }

        [Test]
        public void EmpathyAndCourage_IncreaseHelping_Statistically()
        {
            int helpersHigh = CountHelpers(0.9f), helpersLow = CountHelpers(0.1f);
            Assert.Greater(helpersHigh, helpersLow, "high-empathy/courage agents choose HelpOther more often");
        }

        private static int CountHelpers(float trait)
        {
            int count = 0;
            for (int seed = 0; seed < 20; seed++)
            {
                var world = Fixtures.World(seed);
                var victim = Fixtures.Spawn(world, "Victim", Fixtures.Traits(), "Office B");
                victim.State.SetValue(StateVar.Health, 0.3f);
                var helper = Fixtures.Spawn(world, "Helper", Fixtures.Traits(courage: trait, empathy: trait, risk: trait, fear: 1f - trait), "Office B", new Vector3(2f, 0f, 1f));
                helper.State.SetValue(StateVar.Awareness, 0.8f);
                helper.Beliefs.Observe(FactKey.Alarm, 1f, 1f, 0f, BeliefSource.Perceived);
                for (int i = 1; i <= 20; i++) world.Step(0.1f);
                foreach (var t in helper.Log.Traces) if (t.Chosen != null && t.Chosen.Option.Type == Agents.GoalType.HelpOther) { count++; break; }
            }
            return count;
        }

        [Test]
        public void DecisionTraces_ExplainEveryDecision()
        {
            var sim = new HeadlessSimulation(new ScenarioConfig { Seed = 3, NpcCount = 8, FireDelaySeconds = 5f });
            sim.Run(30f, stopWhenResolved: false);
            foreach (var b in sim.Brains)
                foreach (var t in b.Log.Traces)
                {
                    Assert.IsNotEmpty(t.Options);
                    if (t.Chosen == null) continue;
                    Assert.IsNotEmpty(t.Chosen.Considerations);
                    StringAssert.StartsWith("{", t.ToJson(b.Name));
                }
        }

        [Test]
        public void SameSeed_SameOutcome()
        {
            var a = new HeadlessSimulation(new ScenarioConfig { Seed = 77, NpcCount = 12, FireDelaySeconds = 5f }).Run(90f);
            var b = new HeadlessSimulation(new ScenarioConfig { Seed = 77, NpcCount = 12, FireDelaySeconds = 5f }).Run(90f);
            Assert.AreEqual(a.Summary(), b.Summary());
        }

        [Test]
        public void FullScenario_MostPeopleSurvive_AndSocialBehaviorEmerges()
        {
            var results = ExperimentRunner.Run(new ScenarioConfig { Seed = 100, NpcCount = 24, FireDelaySeconds = 10f }, 5, 240f);
            int safe = 0, npcs = 0, helps = 0, messages = 0;
            foreach (var r in results) { safe += r.Safe; npcs += r.Npcs; helps += r.HelpEvents; messages += r.MessagesDelivered; }
            Assert.Greater((float)safe / npcs, 0.75f);
            Assert.Greater(helps, 0, "someone helped someone");
            Assert.Greater(messages, 0, "people talked");
        }
    }
}
