using Cascade.Agents;
using Cascade.Core;
using NUnit.Framework;
using UnityEngine;

namespace Cascade.Tests
{
    public class AgentModelTests
    {
        [Test]
        public void Beliefs_HearsayIsWeakerThanPerception_AndDecays()
        {
            var store = new BeliefStore();
            var key = FactKey.Hazard(3);
            store.Observe(key, 0.8f, 0.5f, 0f, BeliefSource.Told(new EntityId(9), 1));
            Assert.AreEqual(0.4f, store.Weighted(key, 0f), 1e-4f);
            Assert.Less(store.Weighted(key, 120f), 0.15f, "fire beliefs decay (half-life 60 s)");

            store.Observe(key, 0f, 0.95f, 10f, BeliefSource.Perceived);
            Assert.AreEqual(0f, store.Value(key, 10f));

            var upd = store.Observe(key, 0.9f, 0.3f, 11f, BeliefSource.Told(new EntityId(9), 1));
            Assert.IsFalse(upd.Accepted, "weak hearsay does not override what I just saw");
        }

        [Test]
        public void Beliefs_PerceptionThatContradictsATellerIsReported()
        {
            var store = new BeliefStore();
            var teller = new EntityId(5);
            store.Observe(FactKey.Door(2), (float)(int)World.DoorState.Open, 0.8f, 0f, BeliefSource.Told(teller, 1));
            var upd = store.Observe(FactKey.Door(2), (float)(int)World.DoorState.Blocked, 0.95f, 5f, BeliefSource.Perceived);
            Assert.AreEqual(teller, upd.ContradictedTeller);
        }

        [Test]
        public void Emotion_HasHysteresis()
        {
            var p = Fixtures.Traits();
            var s = new InternalState(p);
            var e = new EmotionModel();
            s.SetValue(StateVar.Fear, 0.6f);
            e.Update(s, 0f, 0.1f);
            Assert.AreEqual(EmotionState.Afraid, e.Current);
            s.SetValue(StateVar.Fear, 0.5f); // below entry (0.55) but above exit (0.45)
            e.Update(s, 0.1f, 0.1f);
            Assert.AreEqual(EmotionState.Afraid, e.Current);
            s.SetValue(StateVar.Fear, 0.4f);
            e.Update(s, 0.2f, 0.1f);
            Assert.AreNotEqual(EmotionState.Afraid, e.Current);
        }

        [Test]
        public void Appraisal_FearfulPeopleAreMoreAffected()
        {
            var brave = Fixtures.Traits(courage: .9f, fear: .1f);
            var anxious = Fixtures.Traits(courage: .1f, fear: .9f);
            var sb = new InternalState(brave);
            var sa = new InternalState(anxious);
            float b0 = sb[StateVar.Fear], a0 = sa[StateVar.Fear];
            Appraisal.Apply(AppraisalEvent.SawFireClose, 1f, sb, brave);
            Appraisal.Apply(AppraisalEvent.SawFireClose, 1f, sa, anxious);
            Assert.Greater(sa[StateVar.Fear] - a0, 3f * (sb[StateVar.Fear] - b0));
        }

        [Test]
        public void Memory_ConsolidatesPlaceDanger_AndForgetsLeastRelevant()
        {
            var m = new MemorySystem { Capacity = 4 };
            m.Record(MemoryKind.SawFire, EntityId.None, 2, 0f, -0.8f, 0.9f);
            Assert.Greater(m.PlaceDanger(2, 0f), 0.8f);
            Assert.Less(m.PlaceDanger(2, 400f), 0.2f);
            for (int i = 0; i < 10; i++) m.Record(MemoryKind.WasWarnedBy, new EntityId(i + 1), 0, i, 0.4f, 0.1f + i * 0.05f);
            Assert.AreEqual(4, m.Count);
            Assert.Greater(m.PlaceDanger(2, 10f), 0.8f, "semantic knowledge survives forgetting the episode");
        }

        [Test]
        public void Relationships_TrustingPeopleForgiveMore()
        {
            var trusting = new RelationshipBook(Fixtures.Traits(trust: 0.9f));
            var suspicious = new RelationshipBook(Fixtures.Traits(trust: 0.1f));
            var other = new EntityId(3);
            float t0 = trusting.Get(other).Trust, s0 = suspicious.Get(other).Trust;
            trusting.Apply(other, SocialEventKind.GaveWrongInfo, 0f);
            suspicious.Apply(other, SocialEventKind.GaveWrongInfo, 0f);
            Assert.Less(t0 - trusting.Get(other).Trust, s0 - suspicious.Get(other).Trust);
        }

        [Test]
        public void Groups_RejectCycles_AndFindRootLeader()
        {
            var g = new GroupRegistry();
            EntityId a = new EntityId(1), b = new EntityId(2), c = new EntityId(3);
            Assert.IsTrue(g.TrySetFollow(b, a));
            Assert.IsTrue(g.TrySetFollow(c, b));
            Assert.IsFalse(g.TrySetFollow(a, c), "a -> c -> b -> a would be a cycle");
            Assert.AreEqual(a, g.RootLeader(c));
            Assert.AreEqual(2, g.FollowerCount(a));
        }
    }
}
