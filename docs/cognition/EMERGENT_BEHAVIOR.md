# Emergent behavior experiments

All experiments are headless, seeded and reproducible (`Simulation/Lab/`), and runnable from the Simulation Lab.

## "Fire blocks the main exit" (`ScenarioLibrary.BlockedMainExit`)

Only initial conditions: Alice (Organizer, employee) knows the secondary exits; Bruno trusts Alice; Caio distrusts her; Dora's friend Lia is injured; Enzo is very curious; Fabi very fearful; everyone else only knows the (burning) main entrance. No per-character behavior is scripted.

Outcome distribution over 20 seeds (first non-routine goal / final state):

| Agent | Most frequent paths |
|---|---|
| Alice | Evacuate→safe 16, HelpOther→safe 4 |
| Bruno | Evacuate→down 12, Evacuate→safe 7 |
| Caio | Evacuate→safe 19 |
| Dora | Evacuate→down 9, Evacuate→safe 5, HelpOther→down 5 |
| Enzo | Evacuate→safe 10, Evacuate→down 7, HelpOther 3 |
| Fabi | Evacuate→safe 10, Evacuate→down 8, Follow 1 |
| Lia (injured) | Evacuate→down 8, Evacuate→safe 6, Follow 6 |

Same scenario, different trajectories per character and per seed; each one is explainable from its trace. (Bruno's poor survival in this configuration is mostly spawn position relative to the fire, visible in his log.)

## Fire drills: does past experience matter?

Same scenario, 4 consecutive episodes with minds carried over (`MindSnapshot`: strategies, dispositions, relationships, place danger). 40 seeds × 12 agents per cell.

| | Episode 1 | Episode 4 |
|---|---|---|
| Learning on | 48 % survive | 60 % |
| Learning off (only memory of places and relationships carried) | 48 % | 55 % |

The learning-specific gain (~5 points) is **suggestive, not conclusive** (≈1.7 standard errors). Carried memory of dangerous places alone already helps.

## Cognitive Experiment (per archetype)

`CognitiveExperiment.Run` reports, per archetype: survival, goal changes, learning events, trust changes, strategy shifts, information seeking, follow and help choices. A test asserts that archetypes differ substantially in social and information-seeking responses to the same scenario.

## Tests of the brief's experiments

Repeated failure, successful strategy, contradictory information, social learning, observed betrayal, unlearning, carry-over, replay reproducibility, and same-seed cognitive trajectory: `Tests/EditMode/CognitionTests.cs`, `CognitiveExperimentTests.cs`.
