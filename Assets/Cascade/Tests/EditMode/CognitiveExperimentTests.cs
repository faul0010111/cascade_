using System.Collections.Generic;
using System.Linq;
using Cascade.Agents;
using Cascade.Core;
using Cascade.Simulation;
using Cascade.Simulation.Headless;
using Cascade.World;
using NUnit.Framework;
using UnityEngine;

namespace Cascade.Tests
{
    /// <summary>Experiments from the cognitive brief (section 22): contradiction, social learning, unlearning, transfer.</summary>
    public class CognitiveExperimentTests
    {
        private static Message Say(AgentBrain from, FactKey key, float value)
        {
            var m = new Message { Sender = from.Id, Kind = MessageKind.Inform, Loudness = 20f, Time = from.Now, SenderPosition = from.Body.Position };
            m.Payload.Add(new Belief { Key = key, Value = value, Confidence = 0.9f, Time = from.Now, Source = BeliefSource.Perceived });
            return m;
        }

        [Test]
        public void ContradictoryInformation_CreatesUncertainty_ThenPerceptionJudgesTheSources()
        {
            var world = Fixtures.World();
            var bodies = new List<HeadlessBody>();
            int eastExit = 9;
            var listener = Fixtures.Spawn(world, "Listener", Fixtures.Traits(curiosity: .9f), "Office B", bodies: bodies);
            var honest = Fixtures.Spawn(world, "Honest", Fixtures.Traits(), "Office B", new Vector3(1f, 0f, 0f), bodies: bodies);
            var liar = Fixtures.Spawn(world, "Liar", Fixtures.Traits(), "Office B", new Vector3(-1f, 0f, 0f), bodies: bodies);
            Fixtures.Run(world, bodies, 0.3f);

            listener.Comms.Receive(Say(honest, FactKey.Door(eastExit), (float)(int)DoorState.Open));
            listener.Comms.Receive(Say(liar, FactKey.Door(eastExit), (float)(int)DoorState.Blocked));
            listener.Tick(world.Now, 0.1f, LodTier.Full);

            Belief b;
            Assert.IsTrue(listener.Beliefs.TryGet(FactKey.Door(eastExit), out b));
            Assert.AreEqual(BeliefStatus.Contested, b.StatusAt(world.Now), "two sources disagree");
            Assert.Greater(b.Doubt(world.Now), 0.4f);
            Assert.IsTrue(listener.Learning.Events.Any(e => e.Kind == LearningEventKind.Belief), "the conflict is noticed");

            // The listener goes and looks (perception is the arbiter): the door is really open.
            float relHonest = listener.Relationships.Get(honest.Id).Reliability, relLiar = listener.Relationships.Get(liar.Id).Reliability;
            var upd = listener.Beliefs.Observe(FactKey.Door(eastExit), (float)(int)DoorState.Open, 0.95f, world.Now + 1f, BeliefSource.Perceived);
            listener.HandleVerification(upd, world.Now + 1f);
            Assert.AreNotEqual(BeliefStatus.Contested, b.StatusAt(world.Now + 1f));
            Assert.IsTrue(upd.ContradictedTeller == liar.Id || upd.ConfirmedTeller == honest.Id, "a source is evaluated");
            Assert.IsTrue(listener.Relationships.Get(liar.Id).Reliability < relLiar || listener.Relationships.Get(honest.Id).Reliability > relHonest);
        }

        [Test]
        public void UncertaintyAboutMyRoute_LeadsToInformationSeeking()
        {
            var world = Fixtures.World();
            var bodies = new List<HeadlessBody>();
            var a = Fixtures.Spawn(world, "Doubter", Fixtures.Traits(curiosity: .9f, independence: .2f), "Office B", bodies: bodies);
            var friend = Fixtures.Spawn(world, "Friend", Fixtures.Traits(), "Office B", new Vector3(1.5f, 0f, 0f), bodies: bodies);
            a.Relationships.SetInitial(friend.Id, RelationshipKind.Colleague, 0.8f, 0.4f);
            a.State.SetValue(StateVar.Awareness, 0.8f);
            a.Beliefs.Observe(FactKey.Alarm, 1f, 1f, 0f, BeliefSource.Perceived);
            Fixtures.Run(world, bodies, 0.5f);
            // Contradictory reports about every exit it knows.
            for (int d = 0; d < world.Building.Graph.DoorCount; d++)
            {
                if (!world.Building.Graph.IsExitDoor(d)) continue;
                a.Beliefs.Observe(FactKey.Door(d), (float)(int)DoorState.Open, 0.6f, world.Now, BeliefSource.Told(friend.Id, 1));
                a.Beliefs.Observe(FactKey.Door(d), (float)(int)DoorState.Blocked, 0.55f, world.Now, BeliefSource.Told(new EntityId(999), 1));
            }
            a.Routes.Invalidate();
            a.RaiseInterrupt("test");
            Fixtures.Run(world, bodies, 1f);
            Assert.Greater(a.Self.Uncertainty, 0.3f);
            bool sought = a.Log.Traces.Any(t => t.Options.Any(o => o.Option.Type == GoalType.Verify || o.Option.Type == GoalType.AskForInfo));
            Assert.IsTrue(sought, "information seeking becomes an option when unsure");
        }

        [Test]
        public void SocialLearning_ObservedSuccessRaisesPreference_LessThanDirectExperience()
        {
            var observer = Fixtures.Spawn(Fixtures.World(), "Observer", Fixtures.Traits(), "Corridor");
            var doer = Fixtures.Spawn(Fixtures.World(), "Doer", Fixtures.Traits(), "Corridor");
            observer.Tick(0.1f, 0.1f, LodTier.Full);
            doer.Tick(0.1f, 0.1f, LodTier.Full);
            var key = StrategyKey.Exit(10);
            float prior = observer.Learning.Predict(key).Expected;
            observer.Learning.Outcome(key, 1f, ExperienceSource.Observed, "saw someone get out", actor: new EntityId(50));
            doer.Learning.Outcome(key, 1f, ExperienceSource.Direct, "got out");
            float observed = observer.Learning.Predict(key).Expected - prior;
            float direct = doer.Learning.Predict(key).Expected - prior;
            Assert.Greater(observed, 0f);
            Assert.Greater(direct, observed, "direct experience weighs more than observation");
        }

        [Test]
        public void ObservingSomeoneGetHurtFollowingALeader_LowersTrustInThatLeader()
        {
            var world = Fixtures.World();
            var bodies = new List<HeadlessBody>();
            var leader = Fixtures.Spawn(world, "Leader", Fixtures.Traits(), "Office B", bodies: bodies);
            var follower = Fixtures.Spawn(world, "Follower", Fixtures.Traits(), "Office B", new Vector3(1f, 0f, 0f), bodies: bodies);
            var witness = Fixtures.Spawn(world, "Witness", Fixtures.Traits(), "Office B", new Vector3(-1f, 0f, 1f), bodies: bodies);
            Fixtures.Run(world, bodies, 0.5f);
            float trustBefore = witness.Relationships.Get(leader.Id).Trust;
            float followBefore = witness.Learning.Predict(StrategyKey.Follow(leader.Id)).Expected;
            world.Groups.TrySetFollow(follower.Id, leader.Id);
            follower.ApplyDamage(0.7f, world.Now);
            Fixtures.Run(world, bodies, 0.3f);
            Assert.Less(witness.Relationships.Get(leader.Id).Trust, trustBefore);
            Assert.Less(witness.Learning.Predict(StrategyKey.Follow(leader.Id)).Expected, followBefore);
            Assert.IsTrue(witness.Learning.Experiences.Items.Any(e => e.Source == ExperienceSource.Observed));
        }

        [Test]
        public void Unlearning_ABlockedExitSeenOpenAgainIsReconsideredAndUsed()
        {
            var world = Fixtures.World();
            var b = Fixtures.Spawn(world, "Unlearner", Fixtures.Traits(), "Corridor", new Vector3(18f, 0f, 0f));
            b.Tick(0.1f, 0.1f, LodTier.Full);
            int exit = b.Routes.PlannedExitDoor(b.Now);
            b.Beliefs.Observe(FactKey.Door(exit), (float)(int)DoorState.Blocked, 0.95f, b.Now, BeliefSource.Perceived);
            for (int i = 0; i < 3; i++) b.Learning.Outcome(StrategyKey.Exit(exit), 0f, ExperienceSource.Direct, "blocked");
            b.Routes.Invalidate();
            Assert.AreNotEqual(exit, b.Routes.PlannedExitDoor(b.Now), "old belief + experience: avoid it");
            float low = b.Learning.Predict(StrategyKey.Exit(exit)).Expected;

            // New evidence: the fire is out and the door is seen open again.
            Fixtures.Run(world, new List<HeadlessBody>(), 0.2f);
            b.Tick(world.Now, 0.1f, LodTier.Full);
            Belief door;
            b.Beliefs.TryGet(FactKey.Door(exit), out door);
            Assert.IsTrue(DoorStates.IsPassable((DoorState)(int)door.Value), "belief revised by perception");
            Assert.IsTrue(door.History.Any(h => h.Kind == RevisionKind.Revised));
            Assert.Greater(b.Learning.Predict(StrategyKey.Exit(exit)).Expected, low, "the failed strategy is reconsidered");
        }

        [Test]
        public void LessonsCarryOverToTheNextEpisode()
        {
            var first = Fixtures.World(1);
            var a = Fixtures.Spawn(first, "Veteran", Fixtures.Traits(), "Corridor", new Vector3(-12f, 0f, 0f));
            a.Tick(0.1f, 0.1f, LodTier.Full);
            int usual = a.Routes.PlannedExitDoor(a.Now);
            for (int i = 0; i < 4; i++) a.Learning.Outcome(StrategyKey.Exit(usual), 0f, ExperienceSource.Direct, "burned last time");
            var snapshot = MindSnapshot.Capture(new[] { a });

            var second = Fixtures.World(2);
            var veteran = Fixtures.Spawn(second, "Veteran", Fixtures.Traits(), "Corridor", new Vector3(-12f, 0f, 0f));
            var novice = Fixtures.Spawn(second, "Novice", Fixtures.Traits(), "Corridor", new Vector3(-12f, 0f, 0f));
            snapshot.Apply(new[] { veteran, novice });
            veteran.Tick(0.1f, 0.1f, LodTier.Full);
            novice.Tick(0.1f, 0.1f, LodTier.Full);
            Assert.AreEqual(usual, novice.Routes.PlannedExitDoor(novice.Now));
            Assert.AreNotEqual(usual, veteran.Routes.PlannedExitDoor(veteran.Now), "same situation, different choice: past experience matters");
        }

        [Test]
        public void SameEvent_DifferentResponses_AcrossPersonalities()
        {
            var g = CognitiveExperiment.Run(new ScenarioConfig { Seed = 70, NpcCount = 24, FireDelaySeconds = 5f }, 4, 150f);
            Assert.Greater(g.Count, 3);
            var rates = g.Values.Select(m => (m.HelpChoices + m.FollowChoices + m.InformationSeeking) / (float)System.Math.Max(1, m.Agents)).ToList();
            Assert.Greater(rates.Max() - rates.Min(), 0.2f, "archetypes respond differently to the same scenario");
        }

        [Test]
        public void LabCommands_ReplayReproducesTheRun()
        {
            System.Func<List<LabCommand>, string> run = cmds =>
            {
                var sim = new HeadlessSimulation(new ScenarioConfig { Seed = 12, NpcCount = 10, FireDelaySeconds = 30f });
                var lab = new LabController(sim.World, (id, p, pos) => { var body = new HeadlessBody(pos); sim.Bodies.Add(body); return body; });
                if (cmds != null) lab.ScheduleReplay(cmds);
                else lab.ScheduleReplay(new[]
                {
                    new LabCommand { Time = 2f, Kind = LabCommandKind.Ignite, Position = new Vector3(36f, 0f, 20f), Value = 0.6f },
                    new LabCommand { Time = 3f, Kind = LabCommandKind.InjectRumor, Int = (int)FactType.DoorState, Subject = 9, Value = (float)(int)DoorState.Blocked, Value2 = 40f, Position = new Vector3(20f, 0f, 12f) },
                });
                while (sim.World.Now < 60f) { lab.Update(); sim.Step(0.1f); }
                string sig = sim.Result().Summary() + "|" + sim.Brains.Sum(b => b.Learning.TotalEvents);
                if (cmds == null) Recorded = new List<LabCommand>(lab.Log);
                return sig;
            };
            string original = run(null);
            string replay = run(Recorded);
            Assert.AreEqual(original, replay);
        }

        private static List<LabCommand> Recorded;
    }
}
