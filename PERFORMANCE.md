# Performance

## Measured (headless, one core, Mono, every agent at full fidelity 10 Hz, no LOD)

| Agents | ms per frame |
|---|---|
| 50 | 0.4 |
| 100 | 1.5 |
| 200 | 4.5 |
| 400 | 18.4 |

A full 24-agent scenario (4 minutes of simulated time) runs in about 0.25 s headless.

These numbers exclude NavMesh, rendering and IMGUI. Re-measure in the Editor with the Profiler and the Lab's "avg/max ms" readout; the Unity numbers will be higher.

## Mechanisms

- **Tick rates by LOD.** Full: 10 Hz decisions / 5 Hz perception. Reduced: 4 Hz / 2 Hz. Minimal: 2 Hz / 1 Hz. LOD is from distance to focus points (camera, player); the selected NPC, panicked or injured agents and any agent with a pending interrupt are always full.
- **Frame budget.** The scheduler stops ticking agents once `FrameBudgetMs` is spent and resumes round-robin next frame (Lab shows deferred ticks).
- **Spatial hash** for perception, hearing, message delivery and witnesses.
- **Belief store indexed by fact type**; threat level cached per tick; needs recomputed only at decision time.
- **Room-graph routing** (12 rooms) instead of NavMesh path queries for strategy; route cache per room/tick.
- **Hazards** stepped at 5 Hz; the hazard view refreshes at 5 Hz with pooled quads.

## Known costs and next steps

- Per-agent belief count grows with the number of agents known (5 facts per known agent). Above ~300 agents, cap `AgentRoom` beliefs to the N most relevant people.
- `StrongestProtectiveUrge` and goal enumeration iterate known agents; move to incremental candidate lists if profiling shows them.
- Perception line-of-sight uses `Physics.Linecast`; batch with `RaycastCommand` for large crowds.
- Allocation: behavior trees are created per plan step. Pool them if GC shows up in the Profiler.

## After the cognitive evolution

Same machine, same session, best of 3, headless full fidelity:

| Agents | Previous release | With cognition |
|---|---|---|
| 50 | 0.51 ms | 0.61 ms |
| 100 | 2.02 ms | 2.11 ms |
| 200 | 8.42 ms | 7.74 ms |

Differences are within run-to-run noise (behavior changes also change the workload). Why the cost stays low:

- Learning is event-driven (outcomes, verifications, witnessed acts, messages), never per frame.
- Strategy tables are bounded (96 entries), histories are bounded (experiences 64, learning events 40, timeline 300 samples at 1 Hz, goal-change traces 48, belief revisions 6 per belief).
- Self-assessment and needs are computed at decision time only.
- Attribution (leave-one-out) costs O(k²) per option with k ≤ 7 considerations, only at decision time.

Watch items: `LearningSystem.Predict` is called from route edge costs (exit edges only, two dictionary lookups); `SelfAssessment.Update` scans door and hazard beliefs (bounded by the building).
