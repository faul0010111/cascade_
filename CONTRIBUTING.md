# Contributing

1. **Keep the layer rules.** `Core`, `AI`, `World`, `Agents` and `Simulation` must not reference MonoBehaviours, scenes or Unity physics. The `.asmdef` files enforce most of this.
2. **Agents never read world truth.** Only `PerceptionSystem` talks to `IPerceptionSource`. If a decision needs a fact, add a belief.
3. **Everything is explainable.** New considerations need readable names; new failure modes need a `Fail("Reason")`.
4. **Determinism.** Use the agent's or system's `Rng` stream, never `UnityEngine.Random` or `System.Random` in simulation code.
5. **Test tendencies.** Behavior tests should run several seeds and assert statistical tendencies, not exact paths.
6. **Run the tests** (Unity Test Runner or `dotnet test Tools/Headless`) before opening a PR, and describe what changed in the NPCs' behavior, ideally with before/after numbers from the Lab batch runner.
7. Code and docs in English. C# style: small classes, XML doc comments that explain *why*.
