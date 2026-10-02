# Roadmap

| Phase | Content | Status |
|---|---|---|
| 1 | Foundation: architecture, core services, world, NavMesh bodies, debug skeleton | done |
| 2 | Perception, beliefs, internal state, emotion | done |
| 3 | Utility goals, GOAP planning, behavior tree execution, decision traces, NPC Debugger | done |
| 4 | Memory, relationships, communication, groups | done |
| 5 | Player as entity, social consequences, Simulation Lab | done (first version) |
| 6 | Claude integration: gateway, validation, dialogue, generators | done (first version) |
| 7 | Emergence tuning with batch experiments | in progress |
| 8 | Performance for hundreds of NPCs | partially: LOD, budget, measurements; see PERFORMANCE.md |

## Cognitive evolution (done in this iteration)

| Phase | Status |
|---|---|
| 0 Audit | done (`docs/cognition/AUDIT.md`) |
| 1 Experience model | done |
| 2 Learning system | done |
| 3 Prediction + prediction error | done |
| 4 Belief revision | done |
| 5 Strategy memory in decisions | done |
| 6 Curiosity / information seeking | done |
| 7 Social learning | done |
| 8 Self-assessment | done |
| 9 Decision Trace 2.0 | done |
| 10 Simulation Lab 2.0 | done (headless API tested; Unity UI compile-checked only) |
| 11 Emergent experiments | done (results in `docs/cognition/EMERGENT_BEHAVIOR.md`) |
| 12 Performance + regression | done (no measurable cost; one regression found and fixed) |
| 13 Documentation | done |

## Next

- Verify and tune everything inside Unity (the pure layers were run and tested headless; the Unity layer was compile-checked against stubs, not run).
- Animations and a proper UI (UI Toolkit) for the debugger.
- Multi-floor buildings (stairs as graph edges).
- Doors agents can close to stop smoke; alarms agents can pull.
- Belief sharing about people's reliability ("don't trust Marcos"): reliability exists now, but is not yet communicated.
- More statistical power for the drills experiment (the learning-specific gain is suggestive, not yet conclusive).
- Use `TrustBias` (currently only reserved) from social outcomes.
- More scenarios beyond fire (power outage, lockdown) reusing the same mind.
- Claude-assisted analysis of exported decision traces (dev assistant).
