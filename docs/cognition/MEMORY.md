# Memory

Episodic memories consolidate into semantic knowledge; experiences (see LEARNING.md) are the episodic record of strategy outcomes.

## Every memory kind and its consumers

| Kind | Consolidates into | Behavior it changes |
|---|---|---|
| SawFire | place danger (intensity) | route costs, danger of rooms, verification targets |
| RouteDangerous | place danger | route costs |
| DoorWasBlocked | place danger (×0.5) | route costs |
| WitnessedInjury | place danger (×0.4) — *no longer* lowers opinion of the victim (bug fixed) | route costs, helping risk |
| LedIntoDanger | place danger (×0.6) + impression of the leader | route costs, trust, Follow |
| WasHelpedBy, LedBy, WasWarnedBy, InfoWasRight | positive impression | bond, leader trust, whom to ask |
| WasAbandonedBy, WasPushedBy, IgnoredBy, InfoWasWrong | negative impression | bond, leader trust, whom to ask |
| SawSomeoneHelp | impression of the helper | bond, leader trust |
| HelpedSomeone, ReachedSafety | episode only (the learning system records the outcome) | via experiences |

Only memories where the subject is **responsible** for what happened shape the impression of that subject (`MemorySystem.ShapesImpression`).

Bounded at 64 episodes; the least relevant (`intensity × recency × match`) is forgotten, semantic knowledge stays. Place danger decays with a 180 s half-life. `MindSnapshot` carries place danger (not episodes) into the next episode.
