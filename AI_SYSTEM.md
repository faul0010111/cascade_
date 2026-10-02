# AI system

How an NPC in Cascade goes from "something happened" to "I am doing this", and how to read it in the debugger.

## 1. Layers and who controls what

| Layer | Question | Technique | Code |
|---|---|---|---|
| Mode | In what state of mind am I? | FSM derived from internal state, with hysteresis | `Agents/InternalState/EmotionModel.cs` |
| Goal | What do I want now? | Utility AI over goal options | `Agents/Goals/Goals.cs` |
| Plan | How do I get it? | GOAP (A* over symbolic state) | `AI/Planning`, `Agents/Actions/AgentAction.cs` |
| Route | Which way do I go? | Dijkstra on the room graph, per-agent costs | `Agents/Navigation/RouteEvaluator.cs` |
| Execute | Which step now? | Small behavior tree per action | `AI/BehaviorTrees`, `Agents/Actions/Behaviors.cs` |
| Move | How does the body get there? | Unity NavMesh (or kinematic headless body) | `Runtime/NpcAgent.cs` |

The mode layer never picks actions. It changes parameters of the others: decision interval, selection temperature, vision cone, planning budget, speed, and which goals are masked (a panicked person does not investigate).

## 2. The tick

Every agent tick (10/4/2 Hz depending on LOD):

1. **Perceive** (5/2/1 Hz): determine visible rooms (current room + rooms seen through open doors), then query the world for fire, smoke, doors, extinguishers and agents in them. Write beliefs, appraisals and memories.
2. **Process** queued sounds, witnessed social acts and received messages.
3. **Update internal state** (decay toward personality baselines) and **emotion** (with hysteresis).
4. **Decide** if the decision timer expired or an **interrupt** was raised (saw fire, route blocked, injured, warned, ordered, being helped, goal achieved/failed). Needs are recomputed first.
5. **Plan** if the goal changed or there is no plan.
6. **Execute** the current behavior tree. Success moves to the next step; failure records a reason (`PathBlocked`, `TargetLost`, `NoAnswer`, `ItemGone`, `Stuck`...) and triggers replanning; three failures put the goal on cooldown.

## 3. How a goal is scored

For each goal definition, the agent enumerates candidate options (e.g. `HelpOther(Ana)`, `HelpOther(Bruno)`), then for each:

```
final = baseWeight
      × Combine(considerations)      // product of curve outputs with Dave Mark's compensation factor
      × personalityMultiplier        // e.g. HelpOther: 0.3 + 1.2·Empathy + 0.2·Courage
      × optionMultiplier             // e.g. Follow: who is calling me and how much I respect them
      × needUrgency                  // 0.25 + 0.75 · max(needs this goal serves)
      × longTermBias                 // e.g. searching for my daughter beats everything else
      × inertia                      // current goal bonus, fading over 30 s
      × failurePenalty               // repeated failures and recently abandoned goals are dampened
```

Considerations read **only beliefs, memory, relationships, needs and state**. There is no path from world truth into a score.

Selection: options within 15 % of the best are candidates; a softmax with the emotion's temperature picks among them (calm ≈ argmax, panicked ≈ noisy). All randomness comes from the agent's own seeded stream, so runs are reproducible.

## 4. Worked example (from a real run)

Nicolas, a Protector (Empathy 100, Courage 82), in the Kitchen when the fire spread:

```
Decision at 22.7s (scheduled)
  > Evacuate               67%
    WarnOther(Karina)      61%
    Follow(Sofia)          38%
    Routine                11%
```

He knew Ana was injured (told by a colleague, 83 % confidence) but did not choose `HelpOther(Ana)`: his own health was near zero, so the `Able` consideration vetoed it. Clicking `WarnOther(Karina)` in the debugger shows each consideration (`I know the danger 1.0`, `Target unaware 0.9`, `Close 0.7`, `Composure 0.6`) and each multiplier. This is the kind of answer the debugger is designed to give.

## 5. Beliefs, memory and communication

- A **belief** is `(fact, value, confidence, time, position, source)`. Confidence decays with a half-life per fact type (fire 60 s, agent location 20 s, exits never).
- Perception overrides hearsay. Weak hearsay does not override recent perception. When perception contradicts something a person told you, that person's trust drops (`GaveWrongInfo`); when it confirms it, trust rises.
- A **message** carries snapshots of the sender's own beliefs, so misinformation propagates naturally. The receiver discounts by trust (`0.35 + 0.65·(trust − 0.5·suspicion)`) and by hop count (`0.85^hops`).
- **Episodic memories** consolidate into semantic knowledge: remembered place danger (half-life 180 s) raises route costs; impressions of people feed trust and bond. Low-relevance episodes are forgotten; the semantic knowledge stays.

## 6. Groups and leadership

There is no group brain. `Follow(X)` is a normal goal, boosted by uncertainty, fear-driven herding, being called by X and respect for X. Leaders are whoever others choose to follow; `GatherGroup` (organizers) broadcasts "follow me" plus everything they know. Reaching safety while following someone creates `LedMeToSafety`. Being left behind while hurt by the person helping you creates `AbandonedMe`.

## 7. Extending

- **New goal:** add a `GoalDefinition` in `GoalLibrary` (enumeration + considerations + multipliers + needs served) and a goal condition in `AgentPlanner.GoalCondition`.
- **New action:** add an `AgentAction` in `ActionLibrary` with preconditions/effects, a belief-based cost and a behavior tree.
- **New fact type:** add it to `FactType`, give it a half-life, write it from perception or communication, read it from considerations.

Always add a test that checks a *tendency* (statistically, over seeds) rather than an exact outcome.

## 8. Learning, prediction and uncertainty (cognitive evolution)

The pipeline above now closes the loop:

- Before an action that tries a **strategy** (an exit, following someone, asking someone, exploring, firefighting, helping, hiding) the agent forms an **expectation**. The outcome produces a **prediction error** that updates what it has learned, per situation (calm / threat / smoke).
- Learned expectations feed decisions: exit route costs, action costs, and considerations such as *Expected outcome* (Follow), *Expected success* (FightFire, HelpOther), *Expected answer* (AskForInfo), *Hiding worked before* (Hide).
- **Uncertainty** (contested or weak beliefs about my route, "something is wrong but I don't know what") raises the information need and enables `Verify` and `AskForInfo`. Curiosity and learned information seeking amplify them; confidence damps them.
- **Self-assessment** (knowledge, plan, decision, social confidence) feeds needs and the follow pull, and lets an agent drop a plan whose expected success collapses.
- The **trace** of every decision now lists signed contributions by category (Need, Emotion, Personality, Relationship, Memory, Prediction, Uncertainty, Belief, Situation, Commitment) and a human reason, e.g. *"Alice led me to safety before (trust in leader)"* or *"I expect exit via East Yard to work (91 %, from experience)"*.

See `docs/cognition/` for the full model.
