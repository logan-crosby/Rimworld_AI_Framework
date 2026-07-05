# RimAI Master Plan — An AI That Plays RimWorld Fully

**Status:** authoritative. Implementing agents start here, then read the layer spec they are assigned.
**Docs:** `01-perception.md`, `02-cognition.md`, `03-action.md`, `04-memory.md`, `05-orchestration.md`
**Skeleton code:** `RimAI.Agent/` project (stubs with comment trees; every public member has a `// TODO(impl)` contract comment).

## 1. Vision

A player-replacement AI: it observes the colony, forms goals, issues the same commands a human player could, and adapts over a full playthrough. The human can dial autonomy from "advisor" (suggests only) to "full player" (acts unattended).

## 2. What exists today (RimAI.Framework — LLM plumbing only)

- `RimAI.Framework.Contracts` + `RimAIApi`: public entry — chat (incl. streaming/SSE, tool calls), embeddings.
- `Core/` ChatManager, EmbeddingManager; `Execution/` HttpExecutor, RetryPolicy, Cache; `Translation/` request/response translators; `Configuration/` BuiltInTemplates (23 provider templates); `UI/` settings.
- It has **zero game-facing capability**. No perception, no actions, no memory, no scheduler.

## 3. Target architecture (new assembly: `RimAI.Agent`, a second mod depending on the Framework)

```
                ┌──────────────────────────────────────────────┐
                │              RimAI.Agent (new mod)           │
                │                                              │
 game state ──▶ │ Perception ──▶ Cognition ──▶ Action          │ ──▶ game commands
 (main thread)  │    │             ▲  │           │            │     (main thread)
                │    ▼             │  ▼           ▼            │
                │  EventBus ──▶ Memory        Verifier         │
                │                                              │
                │        Orchestrator (GameComponent tick,     │
                │        budget, guardrails, autonomy UI)      │
                └───────────────┬──────────────────────────────┘
                                │ IRimAIApi (chat/embeddings, async)
                        RimAI.Framework (existing mod)
```

### Layers and hard boundaries

| Layer | Namespace | Responsibility | Spec |
|---|---|---|---|
| Perception | `RimAI.Agent.Perception` | Main-thread snapshotting of colony state into versioned JSON DTOs; event bus for letters/alerts/damage/deaths | 01 |
| Cognition | `RimAI.Agent.Cognition` | Three-tier planner (Strategic / Tactical / Reactive) driving LLM tool-calling loops; outputs schema-validated `ActionCommand`s only | 02 |
| Action | `RimAI.Agent.Action` | Whitelisted command executor: work priorities, bills, zones, designations, drafting, research, trade, policies; main-thread marshalled; post-execution verification | 03 |
| Memory | `RimAI.Agent.Memory` | Episodic event log + embedding retrieval (via Framework EmbeddingManager); working memory window; save-persisted via `ExposeData` | 04 |
| Orchestration | `RimAI.Agent.Orchestration` | GameComponent driver, cadence scheduler, token/cost budget, safety guardrails, decision log UI, autonomy levels | 05 |

### Non-negotiable technical constraints

1. **Threading:** RimWorld game state is main-thread only. All reads happen in Perception on tick; all writes happen in Action via a main-thread command queue. LLM calls run async off-thread; results re-enter via the queue. Nothing in Cognition may touch `Verse`/`RimWorld` types directly.
2. **Cognition speaks DTOs only.** Perception serializes to plain DTOs; Action deserializes `ActionCommand` DTOs. This makes the planner unit-testable without the game.
3. **Every LLM action is validated** against a JSON-schema whitelist before execution. Unknown/invalid commands are logged and dropped, never guessed.
4. **Budget-capped:** the Orchestrator enforces per-day token/call budgets; degradation order is Strategic → Tactical → Reactive (reactive survives longest).
5. **Save compatibility:** all persistent state lives in `GameComponent`/`ExposeData`; the mod must load into saves that never had it and unload cleanly.

## 4. Decision cadences (Cognition tiers)

| Tier | Trigger | In-game cadence | Scope | Model tier |
|---|---|---|---|---|
| Reactive | EventBus (raid, fire, break, medical) | event-driven, <1 game-hour | draft/undraft, firefight, rescue, retreat | fast/cheap |
| Tactical | scheduler | ~every 6 game-hours | work priorities, bills, zones, hauling, medical ops | mid |
| Strategic | scheduler | ~every 2 game-days | research path, expansion, wealth/threat balance, trade, recruitment | strongest |

## 5. Milestones (implementation order — each ends green-build + testable)

- **M0 — Framework defect burn-down.** (done: raf-vlv/85d/log/ns4)
- **M1 — Perception.** ColonySnapshot DTOs + EventBus. Exit: dev-mode dump of a live colony snapshot as JSON.
- **M2 — Action.** Command queue + whitelist executor + verifier for 10 core commands. Exit: scripted (no-LLM) command file drives the colony.
- **M3 — Reactive loop (first closed loop).** EventBus → reactive planner → Action. Exit: AI drafts colonists and responds to a raid unattended.
- **M4 — Tactical planner.** Work priorities, bills, zones via LLM tool calls. Exit: colony runs 5 game-days without human input, no starvation.
- **M5 — Memory + Strategic planner.** Embedding retrieval feeding strategic prompts. Exit: research + expansion decisions reference past events.
- **M6 — Full autonomy + guardrails + UI.** Autonomy dial, decision log window, budget panel. Exit: full playthrough from landing to stable colony.

Dependencies: M1 and M2 are independent (parallelizable). M3 needs both. M4→M5→M6 sequential.

## 6. Repository layout after this plan

```
RimAI.Framework/           (existing, unchanged apart from bug fixes)
RimAI.Framework.Contracts/ (existing public API)
RimAI.Agent/               (new; skeleton committed by this plan)
  Source/
    Perception/   Cognition/   Action/   Memory/   Orchestration/   UI/
docs/plan/                 (these documents)
```

## 7. Instructions for implementing agents

1. Claim the matching beads issue (`bd ready`); milestones M1–M6 are filed as epics.
2. Read this file + your layer's spec doc. The spec docs contain interface definitions, DTO schemas, and acceptance criteria — implement to those, don't redesign.
3. Skeleton stubs in `RimAI.Agent/` mark every contract with `// TODO(impl)`. Fill stubs; do not rename public members without updating the spec doc.
4. Quality gate: `dotnet build Rim_AI_Framework.sln` with 0 errors/warnings; add unit tests for anything not requiring the game runtime (all of Cognition, DTO serialization, command validation).
5. Session close: follow CLAUDE.md (commit, `bd dolt push`, `git push`).
