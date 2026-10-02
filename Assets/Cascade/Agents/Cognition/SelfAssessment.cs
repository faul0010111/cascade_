using UnityEngine;

namespace Cascade.Agents
{
    /// <summary>
    /// Functional metacognition (not consciousness): how sure the agent is of its knowledge, its plan, its last
    /// decision and the people around it. Recomputed at decision time from beliefs, predictions and relationships.
    /// Consumers: information and assistance needs, Verify / AskForInfo / Follow goals, plan abandonment.
    /// </summary>
    public sealed class SelfAssessment
    {
        public float KnowledgeConfidence = 1f;
        public float PlanConfidence = 0.5f;
        public float DecisionConfidence = 0.5f;
        public float SocialConfidence = 0.5f;
        public FactKey MostDoubted;
        public float MostDoubtedAmount;

        public float Uncertainty => 1f - KnowledgeConfidence;

        public void Update(AgentBrain b, float now)
        {
            // Knowledge: do I know a way out, and how sure am I of the facts that route depends on?
            float route = b.Routes.ExitRouteConfidence(now);
            float doubt = 0f;
            MostDoubtedAmount = 0f;
            foreach (var d in b.Beliefs.OfType(FactType.DoorState))
            {
                bool relevant = b.IsDoorOnCurrentRoute(d.Key.Subject) || b.FloorPlan.IsExitDoor(d.Key.Subject);
                if (!relevant) continue;
                float x = d.HasAlternative ? d.Doubt(now) : 0.5f * d.Doubt(now);
                if (x > MostDoubtedAmount) { MostDoubtedAmount = x; MostDoubted = d.Key; }
                doubt = Mathf.Max(doubt, x);
            }
            foreach (var h in b.Beliefs.OfType(FactType.HazardInRoom))
            {
                if (!h.HasAlternative) continue;
                float x = h.Doubt(now);
                if (x > MostDoubtedAmount) { MostDoubtedAmount = x; MostDoubted = h.Key; }
                doubt = Mathf.Max(doubt, x);
            }
            // "Something is wrong but I don't know what": alarm or awareness without any located threat.
            bool clueless = b.State[StateVar.Awareness] > 0.3f && b.KnownThreatLevel(now) < 0.1f;
            KnowledgeConfidence = b.AtSafety ? 1f : Mathf.Clamp01(route * (1f - 0.7f * doubt) * (clueless ? 0.5f : 1f));

            var active = b.Executor.ActiveStrategy;
            PlanConfidence = active.HasValue ? b.Learning.Predict(active.Value).Expected : (b.Executor.HasPlan ? 0.7f : 0.5f);

            var latest = b.Log.Latest;
            DecisionConfidence = latest != null ? latest.DecisionConfidence : 0.5f;

            float best = 0f;
            foreach (var id in b.Perception.VisibleAgents)
            {
                var r = b.Relationships.Peek(id);
                if (r == null) continue;
                best = Mathf.Max(best, 0.5f * r.Trust + 0.5f * r.Reliability - 0.5f * r.Suspicion);
            }
            SocialConfidence = Mathf.Clamp01(Mathf.Max(best, 0.3f) + 0.5f * b.Learning.Dispositions.SocialExpectation);
        }
    }
}
