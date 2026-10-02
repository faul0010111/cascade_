# Prediction

Before trying a strategy the agent forms a `Prediction { strategy, context, expected, confidence }` (`LearningSystem.Expect`). When the outcome is known, `Outcome()` closes it and computes

```
PredictionError = ObservedOutcome − ExpectedOutcome
```

- The error scales the learning rate (surprise teaches more).
- |error| ≥ 0.5 on direct experience is a **surprise**: appraised (stress/confidence), logged as a learning event, visible in the timeline.
- `LastPredictionError` and a running mean are shown in the inspector and sampled in the cognitive timeline.
- Goal scoring uses predictions directly ("Expected outcome", "Expected success", "Expected answer", "Hiding worked before") and Decision Trace 2.0 reports the prediction behind the chosen option.

Example from a real run: *"Surprised: I tried to follow Vitor: failed (led into danger) expected 75 %, error −0.75"* followed by *"follow Vitor 75 % → 34 % (threat+smoke)"*.
