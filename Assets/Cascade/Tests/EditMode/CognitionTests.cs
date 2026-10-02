using System.Collections.Generic;
using System.Linq;
using Cascade.Agents;
using Cascade.Core;
using Cascade.Simulation;
using Cascade.Simulation.Headless;
using NUnit.Framework;
using UnityEngine;

namespace Cascade.Tests
{
    /// <summary>Experience, prediction and learning (cognitive phases 1-3, 5).</summary>
    public class CognitionTests
    {
        private static AgentBrain Agent(Personality p, string room = "Corridor", Vector3 offset = default(Vector3), SimulationWorld world = null)
        {
            world = world ?? Fixtures.World();
            var b = Fixtures.Spawn(world, "Subject", p, room, offset);
            b.Tick(0.1f, 0.1f, LodTier.Full);
            return b;
        }

        [Test]
        public void PredictionError_IsRecorded_AndSurpriseIsNoticed()
        {
            var b = Agent(Fixtures.Traits());
            var key = StrategyKey.Explore;
            var p = b.Learning.Expect(key);
            var e = b.Learning.Outcome(key, 0f, ExperienceSource.Direct, "test");
            Assert.AreEqual(p.Expected, e.Expected, 1e-5f);
            Assert.AreEqual(-p.Expected, e.PredictionError, 1e-5f);
            Assert.IsTrue(b.Learning.Events.Any(ev => ev.Kind == LearningEventKind.Surprise) == (p.Expected >= LearningSystem.SurpriseThreshold));
        }

        [Test]
        public void RepeatedFailure_LowersPreference_AndTheAgentSwitchesToAnAlternative()
        {
            var b = Agent(Fixtures.Traits(), "Corridor", new Vector3(-12f, 0f, 0f));
            int first = b.Routes.PlannedExitDoor(b.Now);
            Assert.GreaterOrEqual(first, 0);
            float before = b.Learning.Predict(StrategyKey.Exit(first)).Expected;
            for (int i = 0; i < 3; i++)
            {
                b.Learning.Expect(StrategyKey.Exit(first));
                b.Learning.Outcome(StrategyKey.Exit(first), 0f, ExperienceSource.Direct, "blocked");
            }
            b.Routes.Invalidate();
            Assert.Less(b.Learning.Predict(StrategyKey.Exit(first)).Expected, before - 0.3f);
            Assert.AreNotEqual(first, b.Routes.PlannedExitDoor(b.Now), "a strategy known to fail is not chosen again while an alternative exists");
        }

        [Test]
        public void RepeatedSuccess_RaisesPreferenceConfidenceAndSelfConfidence()
        {
            var b = Agent(Fixtures.Traits());
            var key = StrategyKey.Firefight;
            var before = b.Learning.Predict(key);
            for (int i = 0; i < 4; i++) { b.Learning.Expect(key); b.Learning.Outcome(key, 1f, ExperienceSource.Direct, "worked"); }
            var after = b.Learning.Predict(key);
            Assert.Greater(after.Expected, before.Expected);
            Assert.Greater(after.Confidence, before.Confidence);
            Assert.Greater(b.Learning.Dispositions.SelfConfidence, 0f);
        }

        [Test]
        public void SameFailure_ShakesTheFearfulMore_AndMakesTheCuriousSeekInformation()
        {
            var brave = Agent(Fixtures.Traits(courage: .9f, fear: .1f, curiosity: .1f));
            var fearful = Agent(Fixtures.Traits(courage: .1f, fear: .9f, curiosity: .1f));
            var curious = Agent(Fixtures.Traits(courage: .5f, fear: .5f, curiosity: .95f));
            foreach (var b in new[] { brave, fearful, curious })
            {
                b.Learning.Expect(StrategyKey.Explore);
                b.Learning.Outcome(StrategyKey.Explore, 0f, ExperienceSource.Direct, "failed");
            }
            Assert.Greater(fearful.Learning.Dispositions.RiskPerception, 2f * brave.Learning.Dispositions.RiskPerception);
            Assert.Greater(curious.Learning.Dispositions.InformationSeeking, 2f * brave.Learning.Dispositions.InformationSeeking);
            Assert.Less(fearful.Learning.Predict(StrategyKey.Explore).Expected, brave.Learning.Predict(StrategyKey.Explore).Expected,
                "the fearful learn more from failure");
        }

        [Test]
        public void Learning_IsContextual()
        {
            var b = Agent(Fixtures.Traits());
            var key = StrategyKey.Exit(8);
            float now = b.Now;
            for (int i = 0; i < 4; i++) b.Learning.Strategies.Update(key, Contexts.Smoke, 0f, 1f, now);
            for (int i = 0; i < 4; i++) b.Learning.Strategies.Update(key, 0, 1f, 1f, now);
            float calm = b.Learning.Strategies.Predict(key, 0, now).Expected;
            float smoke = b.Learning.Strategies.Predict(key, Contexts.Smoke, now).Expected;
            Assert.Greater(calm - smoke, 0.4f, "same route, different expectation depending on the situation");
        }

        [Test]
        public void Unlearning_NewEvidenceAndTimeReopenAFailedStrategy()
        {
            var b = Agent(Fixtures.Traits());
            var key = StrategyKey.Exit(9);
            for (int i = 0; i < 3; i++) { b.Learning.Expect(key); b.Learning.Outcome(key, 0f, ExperienceSource.Direct, "blocked"); }
            float failed = b.Learning.Predict(key).Expected;
            b.Learning.Reconsider(key, 0.3f, "door seen open");
            float reconsidered = b.Learning.Predict(key).Expected;
            Assert.Greater(reconsidered, failed + 0.15f);
            var late = b.Learning.Strategies.Predict(key, Contexts.General, b.Now + 2000f);
            Assert.Greater(late.Expected, reconsidered, "old lessons fade back toward the prior");
        }

        [Test]
        public void Betrayal_LowersTrustMoreWhenTrustWasHigh_AndChangesFutureFollowing()
        {
            var world = Fixtures.World();
            var bodies = new List<HeadlessBody>();
            var leader = Fixtures.Spawn(world, "Leader", Fixtures.Traits(), "Office B", bodies: bodies);
            var trusting = Fixtures.Spawn(world, "Trusting", Fixtures.Traits(), "Office B", new Vector3(1f, 0f, 0f), bodies: bodies);
            var wary = Fixtures.Spawn(world, "Wary", Fixtures.Traits(), "Office B", new Vector3(-1f, 0f, 0f), bodies: bodies);
            trusting.Relationships.SetInitial(leader.Id, RelationshipKind.Friend, 0.9f, 0.5f);
            wary.Relationships.SetInitial(leader.Id, RelationshipKind.Acquaintance, 0.4f, 0.1f);
            Fixtures.Run(world, bodies, 0.5f);
            float followBefore = trusting.Learning.Predict(StrategyKey.Follow(leader.Id)).Expected;
            foreach (var f in new[] { trusting, wary })
            {
                world.Groups.TrySetFollow(f.Id, leader.Id);
                f.ApplyDamage(0.7f, world.Now);
            }
            float dropTrusting = 0.9f - trusting.Relationships.Get(leader.Id).Trust;
            float dropWary = 0.4f - wary.Relationships.Get(leader.Id).Trust;
            Assert.Greater(dropTrusting, dropWary, "betrayal hurts more when trust was high");
            Assert.Greater(trusting.Relationships.Get(leader.Id).Suspicion, 0.2f);
            Assert.IsTrue(trusting.Memory.Has(MemoryKind.LedIntoDanger, leader.Id));
            Assert.Less(trusting.Learning.Predict(StrategyKey.Follow(leader.Id)).Expected, followBefore - 0.3f);
        }

        [Test]
        public void WitnessingInjury_NoLongerLowersOpinionOfTheVictim()
        {
            var m = new MemorySystem();
            var victim = new EntityId(7);
            m.Record(MemoryKind.WitnessedInjury, victim, 3, 0f, -0.3f, 0.7f);
            Assert.AreEqual(0f, m.Impression(victim), 1e-5f);
            Assert.Greater(m.PlaceDanger(3, 0f), 0.2f, "but the place now feels dangerous");
        }

        [Test]
        public void SameSeed_SameCognitiveTrajectory()
        {
            var a = Trajectory(31);
            var b = Trajectory(31);
            Assert.AreEqual(a.Count, b.Count);
            for (int i = 0; i < a.Count; i++) Assert.AreEqual(a[i], b[i]);
            Assert.Greater(a.Count, 10, "something was learned");
        }

        private static List<string> Trajectory(int seed)
        {
            var sim = new HeadlessSimulation(new ScenarioConfig { Seed = seed, NpcCount = 16, FireDelaySeconds = 5f });
            sim.Run(120f);
            var list = new List<string>();
            foreach (var b in sim.Brains)
            {
                foreach (var e in b.Learning.Events) list.Add(b.Name + "|" + e.Time.ToString("0.00") + "|" + e.Text);
                list.Add(b.Name + "|" + b.Learning.Dispositions);
            }
            return list;
        }
    }
}
