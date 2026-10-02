# Learning

## Strategies

Learning is about **strategies**, small and meaningful, each with consumers:

| Strategy | Parameter | Outcome recorded when | Consumers |
|---|---|---|---|
| ExitRoute | exit door | reaching safety through it (1); route blocked / stuck (0); hurt on the way (0.1) | route edge cost for exit doors |
| FollowPerson | leader | reaching safety while following (1); lost them (0.3); hurt while following (0, betrayal) | Follow goal "Expected outcome", trust |
| AskPerson | person | got an answer (1); ignored (0) | AskForDirections / AskForInfo cost and score |
| ExploreAlone | — | found an exit (1); explored without success (0) | SearchForExit cost |
| FightFire | — | fire out (1); fire too big / out of charge / hurt (0) | FightFire goal and cost |
| HelpPerson | — | carried someone out (1); hurt while going to help (0.1–0.3) | HelpOther goal, carry cost |
| Hide | — | hurt while hiding (0.1) | Hide goal |

Failures that are not the strategy's fault (e.g. "cannot follow someone who follows me", target out of sight when approaching) are **not** learned.

## Update rule

Rescorla–Wagner per (strategy, context) plus a general context-free entry:

```
expected   = blend(prior → general estimate → context estimate, by each one's confidence)
error      = outcome − expected
weight     = sourceWeight × personalitySensitivity(outcome) × (0.6 + 0.8·|error|)
rate       = clamp(weight / (1 + 0.5·evidence), 0.05, 0.8)
value     += rate × (outcome − value)
evidence   decays with a 300 s half-life   → lessons fade (unlearning)
```

- Source weights: Direct 1.0, Observed 0.5, Reported 0.3.
- Sensitivity to failure: `(0.6 + 0.8·Fearfulness) × (1.2 − 0.4·Courage)`; to success: `0.7 + 0.4·Confidence + 0.2·Courage`.
- Contexts: calm / threat / smoke / threat+smoke, from beliefs only.
- Priors depend on personality and relationships (e.g. following someone you trust starts higher).
- **Reconsidering:** new evidence that the world changed (an exit seen open again) softens a lesson back toward the prior, so strategies known to fail are not repeated forever but can be retried when evidence changes.
- **Plan doubt:** if the expected success of the strategy being executed falls below 0.2, the agent stops and reconsiders (once per plan).
- Bounded: 96 entries per agent, least-recently-updated evicted.

## Learned dispositions

Bounded ±0.3 offsets that shift slowly with direct experience. Traits are untouched.

| Disposition | Moved by | Read by |
|---|---|---|
| TrustBias | (reserved for social outcomes) | stranger trust, belief intake from messages |
| RiskPerception | failures, scaled by fearfulness × (1 − courage) | route danger aversion, bravery for helping, fear baseline |
| SelfConfidence | own-strategy success / failure | confidence baseline, priors of solo strategies |
| SocialExpectation | following / asking outcomes | Follow social pull, priors of social strategies, AskForInfo |
| InformationSeeking | failures, scaled by curiosity | information need, Verify, curiosity baseline |

The same failure makes a fearful agent more afraid, a brave one slightly less confident, and a curious one more inclined to check (tested).

## Tests

`Tests/EditMode/CognitionTests.cs` and `CognitiveExperimentTests.cs`: prediction error, repeated failure → alternative, repeated success → confidence, personality-dependent learning, contextual learning, unlearning, betrayal, carry-over between episodes, determinism of the cognitive trajectory.
