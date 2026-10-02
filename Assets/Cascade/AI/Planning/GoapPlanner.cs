using System.Collections.Generic;

namespace Cascade.AI.Planning
{
    /// <summary>A partial assignment over up to 64 boolean symbols.</summary>
    public struct Condition
    {
        public ulong Mask;
        public ulong Values;

        public Condition(ulong mask, ulong values) { Mask = mask; Values = values & mask; }

        public static Condition None => new Condition(0, 0);

        public static Condition Require(int symbol, bool value = true)
        {
            ulong bit = 1UL << symbol;
            return new Condition(bit, value ? bit : 0);
        }

        public Condition And(int symbol, bool value = true)
        {
            ulong bit = 1UL << symbol;
            return new Condition(Mask | bit, value ? (Values | bit) : (Values & ~bit));
        }

        public bool IsSatisfiedBy(ulong state) => (state & Mask) == Values;

        public int CountUnsatisfied(ulong state)
        {
            ulong diff = (state ^ Values) & Mask;
            int count = 0;
            while (diff != 0) { diff &= diff - 1; count++; }
            return count;
        }
    }

    public interface IPlanAction<TContext>
    {
        string Name { get; }
        Condition Preconditions { get; }
        ulong SetBits { get; }
        ulong ClearBits { get; }
        /// <summary>Procedural precondition evaluated against the context (e.g. "an extinguisher is believed to exist").</summary>
        bool IsApplicable(TContext context);
        float Cost(TContext context);
    }

    /// <summary>
    /// Forward A* over symbolic state with a node budget. Agent-agnostic. Costs come from the context,
    /// so different agents produce different plans for the same goal.
    /// </summary>
    public sealed class GoapPlanner<TContext>
    {
        private sealed class Node
        {
            public ulong State;
            public float G;
            public float F;
            public Node Parent;
            public IPlanAction<TContext> Action;
        }

        public int LastExpansions { get; private set; }
        public bool LastBudgetExceeded { get; private set; }

        private readonly List<Node> _open = new List<Node>();
        private readonly Dictionary<ulong, float> _bestG = new Dictionary<ulong, float>();

        public bool Plan(ulong start, Condition goal, IList<IPlanAction<TContext>> actions, TContext context,
                         int budget, List<IPlanAction<TContext>> outPlan)
        {
            outPlan.Clear();
            _open.Clear();
            _bestG.Clear();
            LastExpansions = 0;
            LastBudgetExceeded = false;

            if (goal.IsSatisfiedBy(start)) return true;

            // Cache applicability and cost once per planning call: they depend on context, not on symbolic state.
            int n = actions.Count;
            var applicable = new bool[n];
            var costs = new float[n];
            for (int i = 0; i < n; i++)
            {
                applicable[i] = actions[i].IsApplicable(context);
                costs[i] = applicable[i] ? System.Math.Max(0.01f, actions[i].Cost(context)) : 0f;
            }

            var root = new Node { State = start, G = 0f, F = goal.CountUnsatisfied(start) };
            _open.Add(root);
            _bestG[start] = 0f;
            Node bestPartial = root;

            while (_open.Count > 0)
            {
                int bestIdx = 0;
                for (int i = 1; i < _open.Count; i++) if (_open[i].F < _open[bestIdx].F) bestIdx = i;
                var current = _open[bestIdx];
                _open.RemoveAt(bestIdx);

                if (goal.IsSatisfiedBy(current.State))
                {
                    Reconstruct(current, outPlan);
                    return true;
                }

                if (goal.CountUnsatisfied(current.State) < goal.CountUnsatisfied(bestPartial.State)) bestPartial = current;

                if (++LastExpansions > budget)
                {
                    LastBudgetExceeded = true;
                    break;
                }

                for (int i = 0; i < n; i++)
                {
                    if (!applicable[i]) continue;
                    var a = actions[i];
                    if (!a.Preconditions.IsSatisfiedBy(current.State)) continue;
                    ulong next = (current.State | a.SetBits) & ~a.ClearBits;
                    if (next == current.State) continue;
                    float g = current.G + costs[i];
                    float known;
                    if (_bestG.TryGetValue(next, out known) && known <= g) continue;
                    _bestG[next] = g;
                    _open.Add(new Node { State = next, G = g, F = g + goal.CountUnsatisfied(next), Parent = current, Action = a });
                }
            }

            // No full plan: return the best partial plan so the agent still makes progress. Caller treats it as "struggling".
            if (bestPartial != root) Reconstruct(bestPartial, outPlan);
            return false;
        }

        private static void Reconstruct(Node node, List<IPlanAction<TContext>> outPlan)
        {
            while (node.Parent != null)
            {
                outPlan.Add(node.Action);
                node = node.Parent;
            }
            outPlan.Reverse();
        }
    }
}
