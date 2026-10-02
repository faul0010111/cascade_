# Belief revision

A belief holds: value, confidence, time, position, source, evidence count, contradiction count, an optional **competing claim** (value, confidence, source) and a bounded revision history (6 entries: Formed, Strengthened, Contradicted, Revised).

| Situation | Result |
|---|---|
| New fact | Formed |
| Agreeing evidence | **Strengthened**: `c = 1 − (1 − c_old)(1 − c_new·k)`, k = 0.8 for an independent source, 0.4 for the same one; evidence +1 |
| Own perception disagrees | **Revised**: perception wins; if the old value came from someone, they are evaluated (reliability −) |
| Stronger hearsay disagrees with weak hearsay | **Revised**, old claim kept as competing claim → *contested* |
| Weaker hearsay disagrees | **Contradicted**: not overwritten, confidence reduced, competing claim stored → *contested* |
| Time passes | confidence decays per fact type; below 0.1 → *stale* |

Status: Tentative, Believed, Confirmed, Contested, Stale. `Doubt` (0..1) combines low confidence and competing claims of similar strength.

**Asymmetric caution:** decisions do not ignore a doubtful warning. A credible competing claim that a door is impassable adds route cost proportional to its confidence; a competing claim of fire counts as hazard. Without this, contested warnings caused a measurable rise in casualties (see AUDIT.md).

**Source evaluation:** when perception confirms or contradicts what someone said, that person's `Reliability` moves toward 1 or 0 (bounded rate) and trust changes. Reliability then weighs how much future messages from them are believed.

Consumers of doubt: `SelfAssessment.KnowledgeConfidence`, information need, `Verify` and `AskForInfo` goals, the "Confused" emotion (conflict level).
