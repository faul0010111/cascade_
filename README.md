# Cascade

**An experimental Unity project about autonomous NPCs.** NPCs perceive, believe, remember, feel, relate to each other, decide, plan, talk and cooperate. A building fire is the test bed, not the point.

> Priority: **NPC intelligence > gameplay > graphics.** Everything is primitives and debug overlays on purpose.

## What you will see

- NPCs with 10 personality traits, 9 internal state variables and 8 emotions that change *how* they decide (vision cone, decision rate, randomness, planning horizon).
- No NPC knows the world. Each one has its own **beliefs** (with confidence, source and decay), fed only by what it sees, hears or is told. Rumours travel and degrade with each hop.
- **Memory with consequences**: rooms remembered as dangerous are avoided; people who lied lose trust; people who helped gain it.
- **Hybrid decision making**: Utility AI picks the goal, GOAP plans how, small behavior trees execute, an emotional mode layer modulates everything. Routes are chosen on a room graph with per-agent costs; NavMesh only moves bodies.
- **Emergent social behavior**: leaders emerge from who others choose to follow, groups form, injured people get helped or carried out, warnings spread, some people panic, freeze or hide.
- **Learning from experience**: NPCs form expectations, notice when they are wrong (prediction error), learn which strategies work in which situation, revise beliefs instead of overwriting them, seek information when unsure, learn from watching others, and change their minds about people. See `docs/cognition/`.
- **The player is just another entity**: NPCs perceive, remember and judge what you do.
- **NPC Debugger**: click an NPC to see *why* it chose what it chose, down to each consideration and multiplier.
- **Simulation Lab**: pause/step, time scale, ignite/extinguish, lock doors, spawn archetypes, toggle LOD, export decision traces, run headless batch experiments over many seeds.
- **Optional Claude integration**: dialogue flavor at runtime and NPC/scenario generation at edit time, behind validation, caching, rate limiting, a kill switch and templates. **The game is fully playable without it.**

## Quick start

1. Unity **6000.0 LTS** or newer. Open this folder as a project (Unity resolves the packages in `Packages/manifest.json`: AI Navigation, Input System, Test Framework).
2. If asked to enable the new Input System backends, accept (the code also works with the legacy Input Manager).
3. Menu **Cascade → Create Demo Scene**, then press **Play**. The building, NavMesh, NPCs, player and tools are all created at runtime; there are no hand-made assets.

### Controls

| Key | Action |
|---|---|
| WASD / Shift | move / run |
| E | "Everyone, follow me!" |
| Q | warn nearby people about what *you* have seen |
| F | help the nearest injured person (carries them if they are down) |
| G | push the nearest person (they will remember) |
| Tab | free camera · right-drag orbit · wheel zoom |
| Left click | select an NPC (NPC Debugger) or apply the Lab tool |
| Space / . / R | pause / step / restart |

## Running the tests

- **In Unity:** Window → General → Test Runner → EditMode (43 tests: AI primitives, beliefs and belief revision, memory, emotion, relationships, communication, routing, learning, prediction, social learning, betrayal, unlearning, carry-over, statistical personality tests, determinism of the cognitive trajectory, replay, full scenarios, Claude validators) and PlayMode (smoke test).
- **Without Unity:** `dotnet test Tools/Headless/Cascade.Headless.Tests.csproj` compiles the pure C# layers against a small math stub and runs the same EditMode tests. Used by CI.

## Project layout

```
Assets/Cascade/
  Core/               ids, clock, event bus, seeded RNG, spatial hash          (no Unity)
  AI/                 Utility AI, behavior trees, GOAP                         (generic, no agent knowledge)
  World/              room graph, doors, fire/smoke model, interactables       (world truth)
  Agents/             the NPC mind: perception, beliefs, memory, personality, state, emotion,
                      needs, goals, planning, actions, relationships, communication, groups, traces
  Simulation/         engine-independent world driver, routing, LOD scheduler, scenario builder,
                      headless runner and batch experiments
  ClaudeIntegration/  gateway, prompts, validators, dialogue director, templates
  Runtime/            thin Unity adapters: NavMesh bodies, player, building builder, views, bootstrap
  Debug/              NPC Debugger overlay, Simulation Lab, runtime gizmos
  Editor/             menu, NPC Inspector window, Claude Generator window
  Tests/              EditMode and PlayMode tests
Tools/Headless/       .NET test project that runs the pure layers without Unity
```

Read **ARCHITECTURE.md** first, then **AI_SYSTEM.md** and **NPC_DESIGN.md**.

## Documentation

| File | Contents |
|---|---|
| [ARCHITECTURE.md](ARCHITECTURE.md) | layers, principles, data flow, decisions and implementation deltas |
| [AI_SYSTEM.md](AI_SYSTEM.md) | how a decision is made, step by step, with a worked example |
| [NPC_DESIGN.md](NPC_DESIGN.md) | traits, archetypes, emotions, needs, goals, actions, how to add behaviors |
| [CLAUDE_INTEGRATION.md](CLAUDE_INTEGRATION.md) | what Claude does and never does, setup, security, validation |
| [PERFORMANCE.md](PERFORMANCE.md) | measurements, LOD, budgets, profiling |
| [ROADMAP.md](ROADMAP.md) | phases, what is done, what is next |
| [CONTRIBUTING.md](CONTRIBUTING.md) | rules for contributions |
| [docs/cognition/](docs/cognition/) | cognitive model, learning, memory, belief revision, social learning, prediction, emergent experiments, audit |

## License

MIT, see [LICENSE](LICENSE).
