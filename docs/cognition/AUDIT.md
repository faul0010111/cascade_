# Phase 0 — Repository audit (cognitive evolution)

Audit performed on the code, not on the docs. Baseline recorded before any change.

## Baseline

| Metric | Value |
|---|---|
| C# | 74 files, ~9.6k lines, 11 assemblies |
| EditMode tests | 26 / 26 passing (headless runner) |
| 10 seeds × 24 NPCs, fire at 10 s | safe 95 %, 1.2 incapacitated/run, resolved in 63 s, 13.3 help events/run, 2.3 groups/run |
| Full-fidelity cost, headless | 100 NPCs 2.1 ms/frame, 200 NPCs 8.5 ms/frame (this machine, this session) |
| TODO / FIXME / mocks in production code | none |

## CURRENT IMPLEMENTATION

Implemented and exercised by tests or headless runs:

- Perception with visibility-first queries (`IPerceptionSource` is the only path to truth); FOV narrowed by stress, range by smoke.
- `BeliefStore`: value, confidence, half-life decay per fact type, source (Prior/Perceived/Told/Inferred, hops), per-type index, one-shot verification of hearsay (teller confirmed/contradicted).
- Episodic memory with consolidation into place danger and person impressions; bounded with relevance-based forgetting.
- Personality (10 traits), internal state (9 vars), appraisal table, emotion FSM with hysteresis, needs (8).
- Utility goal selection (10 goals) with compensation, personality/option multipliers, need urgency, long-term bias, inertia, failure and abandonment damping; full `DecisionTrace`.
- GOAP planning over 16 actions, behavior-tree execution with structured failure reasons, per-agent room-graph routing.
- Relationships (6 dimensions), communication of belief snapshots with trust and hop discount, emergent groups.
- Engine-independent `SimulationWorld`, LOD scheduler with frame budget, headless runner, batch experiments, NPC Debugger, Simulation Lab, optional Claude layer with validation.

## ARCHITECTURE GAPS (against the new cognitive loop)

| Gap | Evidence |
|---|---|
| **No experience model.** Action outcomes are logged as text and immediately forgotten. | `ActionExecutor` returns `Failed` + reason; nothing records context, expectation or outcome. |
| **No learning.** The same failing strategy is chosen again after cooldowns; nothing is learned from success. | Goal failure only increments a counter that is cleared on success/cooldown. |
| **No predictions / prediction error.** | No expected outcome exists anywhere. |
| **Belief revision is replacement.** A perceived value overwrites; hearsay either overwrites or is dropped. No evidence count, no contradiction record, no "contested" state, no history. | `BeliefStore.Observe`. |
| **Uncertainty is implicit.** Confidence exists per belief but there is no agent-level notion of "I don't know / my info may be wrong", and nothing seeks to verify a doubtful belief. | `Investigate` only targets noise and smoke. |
| **Social learning is minimal.** Witnessed help changes respect; nobody learns *strategies* (routes, whom to follow) from watching others. | `PerceptionSystem.ProcessQueued`. |
| **No self-assessment.** | — |
| **Decision traces explain scores but not influences** (no per-factor contribution, no category like "memory" or "prediction", no decision confidence, no human reason). | `OptionTrace`. |
| **Inspector shows only the present.** No timelines, no way to ask "why at t = 42 s". | `BrainReport`. |
| **Lab cannot run cognitive experiments** (clone, compare, freeze, inject beliefs/false information, replay, multi-episode learning). | `SimulationLabPanel`. |

## Memory kinds without real consumers ("decorative memory")

Consolidation feeds place danger (SawFire, RouteDangerous, DoorWasBlocked) and a single scalar impression per person (every kind with a subject and a valence). Everything else has no consumer:

| Kind | Problem |
|---|---|
| ReachedSafety | no subject, no place danger: read by nothing |
| WitnessedInjury | **bug:** stored with valence −0.3 and the injured person as subject, so seeing someone hurt *lowers your opinion of them* |
| HelpedSomeone | subject = the person I helped, valence +0.6: raises my impression of *them*, which is not what it means |
| WasWarnedBy, InfoWasRight/Wrong, IgnoredBy, LedBy, WasPushedBy, SawSomeoneHelp, WasAbandonedBy | only reach behavior through the generic impression scalar, duplicated by the explicit relationship update done at the same call site |

Also: `Relationship.Dependency` is written but never read; `SocialEventKind.FollowedMe` and `RefusedMyOrder` are never emitted; `EmotionModel.AddConflict` has a single caller.

## EXISTING SYSTEMS TO REUSE

- `DecisionLog` ring buffers → extend with experiences, learning events and timelines.
- `GoalSelector` pipeline → add prediction/uncertainty considerations and attribution without changing its shape.
- `AgentAction.CostFn` → the natural place for learned costs; `RouteEvaluator.EdgeCost` → learned route preferences.
- `BeliefUpdate.ContradictedTeller/ConfirmedTeller` → generalize into belief revision + source reliability.
- `SimulationWorld.PublishSocialAction` + witness routing → observed experiences.
- `HeadlessSimulation` / `ExperimentRunner` → cognitive experiments and multi-episode learning.
- `Rng` streams → all new stochastic choices.

## DUPLICATIONS

- Danger is computed in three places with different formulas (`DangerAt`, `KnownThreatLevel`, `HazardSeverity` + needs). Kept, but new code must go through `DangerAt`/`KnownThreatLevel` only.
- Person evaluation appears in `Bond`, `LeaderTrust`, `BestPersonToAsk`, each mixing trust, respect, suspicion and impression differently. Will be centralized as a single "reliability as a source/leader" estimate fed by learning.
- Impression (memory) and relationship both encode "how I feel about X" and are both updated from the same events.

## RISKS

- **Runaway learning** (one bad experience locks a strategy out forever) → bounded learning rates, decay toward prior, evidence-triggered re-openness, tests for unlearning.
- **Loss of determinism** → learning uses no randomness; every update is event-driven from deterministic events; add a trajectory determinism test.
- **Explainability erosion** → every learned number carries its history (bounded) and appears in traces with a category.
- **Performance** → learning is event-driven (outcomes, verifications, witnessed events), tables are bounded, timelines are sampled at 1 Hz; measure after each phase.
- **Regressions in the tuned baseline** → re-run the 10-seed baseline after each phase; large shifts must be explained.

## IMPLEMENTATION PLAN

New code lives in `Agents/Cognition/` (same assembly: cognition is part of the mind, and it must not see world truth).

1. Experience model: `Experience` (context, strategy, expectation, outcome, reward, prediction error, emotion, confidence, social context, place, time, source Direct/Observed/Reported) + bounded `ExperienceLog`.
2. Learning: `StrategyModel` (Rescorla–Wagner value per strategy × context bucket with a general fallback, confidence, bounded, decaying toward prior) + `LearningSystem` (single entry point, personality-modulated rates, dispositions).
3. Prediction: every strategy attempt records a `Prediction`; outcomes produce `PredictionError`; surprise feeds learning rate, appraisal and memory.
4. Belief revision: evidence/contradiction counts, contested state with the competing claim, status, bounded history, source reliability.
5. Strategy memory wired into decisions: route costs, action costs, goal considerations; evidence-triggered re-openness.
6. Uncertainty and curiosity: knowledge confidence, contested beliefs → `Verify` / `Ask` behavior.
7. Social learning: observed exits, observed followers getting hurt, reported outcomes; direct > observed > reported.
8. Self-assessment: decision / plan / knowledge / social confidence with consumers.
9. Decision Trace 2.0: per-factor attribution by category, decision confidence, human reason, predictions.
10. Inspector timelines and "why at time t".
11. Simulation Lab 2.0 through a recordable, replayable `LabCommands` API.
12. Emergent experiments + the six learning tests from the brief + trajectory determinism.
13. Performance and regression, documentation.

## Regression found and fixed during implementation

After phases 4–8, the 10-seed baseline dropped (safe 95 % → 91 %, incapacitated 1.2 → 2.2 per run). Method used:

1. **Ablation** (`CognitionSettings`): with every new mechanism switched off the drop persisted, so learning was not the cause.
2. **Cause 1 — belief revision without caution.** A warning "this door is blocked" now made the belief *contested* instead of replacing it, but the route planner still read only the main value. Fix: a credible dangerous alternative claim adds route cost and counts as hazard (asymmetric caution).
3. **Cause 2 — a bug fix removed an accidental brake.** Seeing someone injured used to *lower the observer's opinion of the victim*; removing that bug nearly doubled helping decisions (69 → 123 over 30 seeds), in riskier situations. Collapse analysis showed "went to help, got hurt" as the dominant cause. Fix: risk of helping now uses the danger of the whole believed route to the victim, getting hurt on the way is learned, and the stranger bond floor was recalibrated (0.55 → 0.45).
4. **Result (30 seeds, same seeds as the previous release):** incapacitated 1.00 ± 0.26 per run vs 0.97 ± 0.35 before; safe 96 % vs 96 %.
