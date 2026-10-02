using System.Collections.Generic;
using Cascade.AI.BehaviorTrees;
using Cascade.AI.Planning;
using Cascade.AI.Utility;
using Cascade.Core;
using NUnit.Framework;

namespace Cascade.Tests
{
    public class AiPrimitivesTests
    {
        [Test]
        public void ResponseCurves_AreClampedAndMonotonic()
        {
            var lin = ResponseCurve.Linear;
            Assert.AreEqual(0f, lin.Evaluate(-1f), 1e-5f);
            Assert.AreEqual(1f, lin.Evaluate(2f), 1e-5f);
            var inv = ResponseCurve.InverseLinear;
            Assert.Greater(inv.Evaluate(0.2f), inv.Evaluate(0.8f));
            var logistic = ResponseCurve.Logistic(10f, 0.5f);
            Assert.Less(logistic.Evaluate(0.3f), 0.5f);
            Assert.Greater(logistic.Evaluate(0.7f), 0.5f);
            Assert.AreEqual(0.4f, ResponseCurve.Floor(0.4f).Evaluate(0f), 1e-5f);
        }

        [Test]
        public void Compensation_DoesNotPunishOptionsWithMoreConsiderations()
        {
            float two = UtilityMath.Combine(new List<float> { 0.8f, 0.8f });
            float six = UtilityMath.Combine(new List<float> { 0.8f, 0.8f, 0.8f, 0.8f, 0.8f, 0.8f });
            float naiveSix = 0.8f * 0.8f * 0.8f * 0.8f * 0.8f * 0.8f;
            Assert.Greater(six, naiveSix * 1.5f);
            Assert.Less(System.Math.Abs(two - six), 0.25f);
            Assert.AreEqual(0f, UtilityMath.Combine(new List<float> { 1f, 0f, 1f }), "a zero consideration vetoes");
        }

        [Test]
        public void Selector_WithZeroTemperature_IsArgmax_AndIsDeterministicPerSeed()
        {
            var scores = new List<float> { 0.2f, 0.9f, 0.85f };
            Assert.AreEqual(1, UtilitySelector.Select(scores, 0f, 0.15f, new Rng(1)));
            int a = UtilitySelector.Select(scores, 0.3f, 0.15f, new Rng(42));
            int b = UtilitySelector.Select(scores, 0.3f, 0.15f, new Rng(42));
            Assert.AreEqual(a, b);
            Assert.AreNotEqual(0, a, "options outside the margin are never picked");
        }

        private sealed class Act : IPlanAction<object>
        {
            public string Name { get; set; }
            public Condition Preconditions { get; set; }
            public ulong SetBits { get; set; }
            public ulong ClearBits { get; set; }
            public float C = 1f;
            public bool IsApplicable(object c) => true;
            public float Cost(object c) => C;
        }

        [Test]
        public void Goap_FindsCheapestPlan_AndPartialPlanWhenImpossible()
        {
            var actions = new List<IPlanAction<object>>
            {
                new Act { Name = "GetKey", SetBits = 1UL << 0, C = 1f },
                new Act { Name = "OpenDoor", Preconditions = Condition.Require(0), SetBits = 1UL << 1, C = 1f },
                new Act { Name = "BreakDoor", SetBits = 1UL << 1, C = 5f },
            };
            var planner = new GoapPlanner<object>();
            var plan = new List<IPlanAction<object>>();
            Assert.IsTrue(planner.Plan(0, Condition.Require(1), actions, null, 100, plan));
            Assert.AreEqual(2, plan.Count);
            Assert.AreEqual("GetKey", plan[0].Name);

            Assert.IsFalse(planner.Plan(0, Condition.Require(0).And(5), actions, null, 100, plan));
            Assert.AreEqual("GetKey", plan[0].Name, "returns the best partial plan");
        }

        [Test]
        public void BehaviorTree_SequenceStopsOnFailure_TimeoutFails()
        {
            int ran = 0;
            var seq = new Sequence<object>("s",
                new Do<object>("a", (c, dt) => { ran++; return BtStatus.Success; }),
                new Condition<object>("fail", c => false),
                new Do<object>("never", (c, dt) => { ran += 100; return BtStatus.Success; }));
            Assert.AreEqual(BtStatus.Failure, seq.Tick(null, 0.1f));
            Assert.AreEqual(1, ran);

            var t = new Timeout<object>(new Do<object>("forever", (c, dt) => BtStatus.Running), 1f);
            for (int i = 0; i < 9; i++) Assert.AreEqual(BtStatus.Running, t.Tick(null, 0.1f));
            Assert.AreEqual(BtStatus.Running, t.Tick(null, 0.05f));
            Assert.AreEqual(BtStatus.Failure, t.Tick(null, 0.2f));
        }

        [Test]
        public void Rng_StreamsAreIndependentAndReproducible()
        {
            var a1 = Rng.Stream(7, "agent:1");
            var a2 = Rng.Stream(7, "agent:1");
            var b = Rng.Stream(7, "agent:2");
            Assert.AreEqual(a1.NextULong(), a2.NextULong());
            Assert.AreNotEqual(a1.NextULong(), b.NextULong());
        }
    }
}
