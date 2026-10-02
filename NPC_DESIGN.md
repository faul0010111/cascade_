# NPC design

## Personality (static, 0..1)

| Trait | Mainly affects |
|---|---|
| Courage | risk vs bravery for helping and firefighting, fear sensitivity, route danger aversion |
| Fearfulness | fear sensitivity, baseline fear, Hide, Evacuate |
| Leadership | GatherGroup (quadratic), WarnOther, less likely to follow |
| Empathy | HelpOther, protective urge, willingness to answer questions |
| Curiosity | Investigate, curiosity baseline |
| Aggression | anger sensitivity, a little firefighting |
| Trust | initial trust in strangers, how much trust changes up or down |
| RiskTolerance | route danger aversion, firefighting |
| Patience | stress sensitivity and recovery |
| Independence | following, asking for directions, social need |

Archetypes (`Agents/Personality/Personality.cs`) are Gaussian distributions over traits, not fixed profiles: Protector, Anxious, Organizer, Loner, Curious, Average. Each has a chance of being an employee (knows every exit).

## Internal state (dynamic, 0..1)

Stress, Fear, Energy, Health, Confidence, Awareness, Curiosity, Aggression, SocialNeed. Each drifts back to a personality-dependent baseline. Events change them through the **appraisal table** (`InternalState.cs`), scaled by personality: the same fire frightens an anxious person several times more than a brave one (a test checks it).

## Emotions (derived)

| Emotion | Enters when | Effect |
|---|---|---|
| Calm | default | normal |
| Nervous | fear > .25 or stress > .35 | slightly faster decisions |
| Afraid | fear > .55 | faster, narrower vision, no Routine/Investigate |
| Panicked | fear > .8 and stress > .7 | very fast noisy decisions, 2-step plans, tunnel vision, only survival-type goals |
| Confident | confidence > .65 and aware | bigger planning budget |
| Angry | aggression > .65 | noisier choices |
| Confused | aware, contradictory information, low confidence | slow decisions, smaller plans |
| Relieved | fear dropped > .3 in the last 6 s | slower, calmer |

Exit thresholds are lower than entry thresholds (hysteresis).

## Needs

Safety, Survival, Social, Information, Rest, Assistance, Escape, ProtectOthers. Recomputed from beliefs and state at each decision. Goals declare which needs they serve.

## Goals

Routine, Evacuate, HelpOther(target), Follow(target), SearchFor(target), Investigate(room), WarnOther(target), GatherGroup, FightFire(room), Hide.

Long-term goals persist across decisions. Currently: `FindPerson` (family members created by the scenario or by Claude-generated profiles).

## Actions

TravelToSafety, SearchForExit, AskForDirections, GoToTarget, SearchForTarget, AssistTarget, CarryToSafety, WarnTarget, FetchExtinguisher, GoToFire, ExtinguishFire, FollowLeader, HideInPlace, InvestigatePlace, GatherNearby, Routine.

## Relationships

Per pair, sparse: Trust, Fear, Respect, Affection, Dependency, Suspicion, plus a kind (Stranger → Acquaintance, Colleague, Friend, Family) and a history. They change only through social events: HelpedMe, AbandonedMe, GaveCorrectInfo, GaveWrongInfo, LedMeToSafety, PushedMe, HelpedOther (witnessed), WarnedMe, IgnoredMyRequest, FollowedMe, RefusedMyOrder.

## Design rules

1. Personality never selects behavior directly; it weights, scales and thresholds.
2. Every memory kind must be read by something that changes behavior.
3. Decisions read beliefs, never the world.
4. Tune with the Simulation Lab batch runner and assert tendencies in tests, not exact outcomes.

## Adaptive (learned) layer

On top of stable traits, each NPC accumulates: strategy expectations per situation, learned dispositions (trust bias, risk perception, self-confidence, social expectation, information seeking; bounded ±0.3), per-person reliability, and a cognitive history. Personality decides *how much* experience moves them: the same failure shakes a fearful NPC more, barely moves a brave one, and makes a curious one want to check more.

New goals: **Verify(place)** (go look to resolve doubt) and **AskForInfo(person)** (ask someone credible). New relationship dimension: **Reliability**.
