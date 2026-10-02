# Social learning

Three sources, with decreasing weight:

| Source | Weight | Examples |
|---|---|---|
| Direct | 1.0 | I got out through the east exit; the person I followed led me into fire |
| Observed | 0.5 | I saw Pedro get out through the east exit; I saw someone get hurt following Alice; I saw Alice lead someone out |
| Reported | 0.3 | Carla says the east exit is blocked |

Mechanics:

- **Observed exits:** when perception sees an agent move from an interior room to outside, the observer records an Observed success for that exit door (once per observed person).
- **Observed leadership:** a follower who gets hurt publishes a visible `Endangered` act about the leader; one who reaches safety publishes `Led`. Witnesses update trust/respect toward the leader and their own expectation of following that leader.
- **Reported:** a message that an exit is blocked is a Reported failure of that exit, besides updating the belief.
- **Source reliability:** verified and falsified claims move each person's `Reliability`.
- **Betrayal:** being led into harm by a trusted leader drops trust by an amount that grows with prior trust, raises suspicion, reduces dependency, creates a `LedIntoDanger` memory and lowers the Follow expectation. Tested.

NPCs never read another NPC's memory: everything above arrives through perception or messages.
