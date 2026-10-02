# Claude integration

Claude is an optional, high-level layer. **It never controls agents frame by frame, never decides mechanics, and the game runs completely without it.**

## What it does

| Use | When | Model (default) | Fallback |
|---|---|---|---|
| Dialogue flavor for messages NPCs actually send | runtime, only speakers at full LOD | `claude-haiku-4-5-20251001` | template lines |
| NPC profile generation | edit time (Cascade → Claude Generator) | `claude-sonnet-5` | archetype sampling |
| Scenario generation | edit time | `claude-sonnet-5` | hand-written ScenarioConfig |

Model ids are plain strings in `ClaudeSettings` so you can change them. Check the current list at https://docs.claude.com/en/docs/about-claude/models/overview.

## Guarantees

- **Mechanics first, words second.** A message's effect (beliefs transferred, trust changes) happens immediately. The generated line only replaces the template text if it arrives within `DialogueDeadlineSeconds` (2.5 s); late lines are discarded.
- **No world leaks.** Dialogue prompts are built only from the speaker's beliefs, emotion, speech style and relevant memories (`PromptBuilder.DialogueUser`).
- **Untrusted output.** Responses are parsed with a small JSON reader and validated field by field: enums against allowlists, numbers clamped, strings length-limited and stripped, rooms and doors checked against the building, relationship names checked against the batch. Invalid entries are dropped with a readable report.
- **Rate limiting, caching, kill switch.** Requests per minute are capped; identical requests are cached; a 401/403 trips the kill switch; the Lab panel has a manual kill switch.
- **Load-time validation.** Generated JSON files are validated again when the bootstrap loads them.

## Setup

1. Get an API key from the Claude Console.
2. Editor: Cascade → Claude Generator → paste the key (stored in EditorPrefs on your machine, never in the repo). Alternatively set the `ANTHROPIC_API_KEY` environment variable.
3. Runtime dialogue: in the `CascadeBootstrap` inspector, enable `Claude.Enabled` and `UseForDialogue`.
4. **Player builds must not contain a key.** Put a small server-side proxy in front of the API that adds the key, and set `ProxyEndpoint`; the gateway then sends no key header.

The request format is the standard Messages API: `POST /v1/messages` with `x-api-key`, `anthropic-version: 2023-06-01`, and a JSON body with `model`, `max_tokens`, `system` and `messages`. See https://docs.claude.com/en/api/messages.

## Workflow for generated content

1. Generate in the Claude Generator window and read the validation report.
2. Save; the file goes to `Assets/Cascade/Data/Generated/` so you can review and version it.
3. Assign an NPC file to `CascadeBootstrap.GeneratedProfiles`. Scenario files contain a `.scenario.txt` with the validated config to copy into the bootstrap.

## Code

`ClaudeIntegration/ClaudeGateway.cs` (transport-agnostic gateway), `Transport/UnityWebRequestTransport.cs`, `Validators.cs`, `PromptBuilder.cs`, `DialogueDirector.cs`, `MiniJson.cs`. Tests: `Tests/EditMode/ClaudeIntegrationTests.cs` (uses a fake transport, never calls the network).
