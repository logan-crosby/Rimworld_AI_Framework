# RimAI Agent — Implementation Playbook (Index)

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Take the RimAI Agent mod from its current state (M1 Perception code-complete) through M6 Full Autonomy, with every milestone implemented, tested, and committed.

**Architecture:** Layered agent: Perception (main-thread snapshots + EventBus) → Cognition (tiered LLM planners) → Action (whitelisted command queue + verifier), orchestrated by a GameComponent with cadence scheduling and memory. All Verse/RimWorld reads and writes happen on the main thread; LLM work is async off-thread against plain DTOs only.

**Tech Stack:** C# (net472 for RimWorld + net8.0 test target), Krafs RimWorld reference assemblies, Harmony, Newtonsoft.Json, xUnit, RimAI.Framework (LLM gateway) via RimAI.Framework.Contracts.

---

## How to use this playbook

1. Execute plan files in the order listed below. Do not start a file until its prerequisites are ✅.
2. Within a file, execute tasks in order, checking off steps as you go. TDD: test first, watch it fail, implement, watch it pass, commit.
3. **Contract authority** (highest first):
   1. Code already in the repo (`RimAI.Agent/Source/Action/ActionContracts.cs`, `ActionCommand.cs`, `RimAI.Agent/Source/Perception/*`) — plan code MUST match these signatures exactly unless a task explicitly modifies them.
   2. The Architecture Decisions (AD-1…AD-7) in this index — they override the spec docs where they conflict.
   3. `docs/plan/06-plan-audit.md` gap resolutions (G-xx) — they override 00–05 where they conflict.
   4. The spec docs `docs/plan/00`–`05`.
4. Track work in beads (`bd`): each plan file has a tracking issue (listed below); claim it before starting, close it when the file's final validation gate passes. Never use markdown TODO lists.
5. Quality gate after every task: `dotnet build RimAI.Agent/RimAI.Agent.csproj && dotnet test RimAI.Agent.Tests/RimAI.Agent.Tests.csproj` — both targets must be green before committing.

## Execution order

| # | Plan file | Scope | Prereqs | Tracking issue |
|---|-----------|-------|---------|----------------|
| 1 | `01-m1-closeout.md` | Commit M1, verify M0.5, doc status sync, close beads | none | raf-jo2 |
| 2 | `02a-m2-core.md` | EntityResolver, command-type expansion, staleness guard, queue, dispatcher + their tests | 01 | raf-ad9 |
| 3 | `02b-m2-handlers.md` | Core-12 command handlers + validation tests | 02a | raf-ad9 |
| 4 | `02c-m2-pipeline.md` | Verifier, executor wiring, feedback, dev tooling, integration tests | 02b | raf-ad9 |
| 5 | `03-m3-reactive.md` | Reactive cognition tier, prompt assembly, tool-calling loop, budget manager, first closed loop | 02c | raf-2vs |
| 6 | `04-m4-tactical.md` | Tactical tier, cadence scheduler, GameComponent tick driver, snapshot-diff integration | 03 | see file |
| 7 | `05-m5-memory.md` | Episodic log, embedding retrieval, working memory + persistence, strategic tier, decision log | 04 | see file |
| 8 | `06-m6-autonomy.md` | Autonomy dial, decision log window, budget panel, guardrails, handler backlog | 05 | see file |

If a tracking issue shown as "see file" does not exist yet, the plan file's first task creates it with `bd create`.

## Architecture Decisions (binding)

These resolve every known spec ambiguity. Plan files and implementations MUST follow them. Where a spec doc disagrees, the spec doc is stale — fix the spec doc in the errata task (01-m1-closeout.md Task 4) rather than following it.

- **AD-1 — `work_type` parameter format:** `WorkTypeDef.defName` (e.g. `"Firefighter"`, `"Doctor"`, `"Hauling"`). Rationale: snapshot DTOs already expose `DefName` fields, so defNames are what the LLM sees. Handlers resolve via `DefDatabase<WorkTypeDef>.GetNamedSilentFail(name)`; `null` result → `FailureCategory.ValidationError` with message listing the invalid name. The same defName rule applies to every `*_def_name` parameter (recipes, plants, floors, blueprints).
- **AD-2 — `CreateZone` parameters:** rect form `x, z, w, h` (ints, max 20×20) per G-21. The parameter table in `docs/plan/03-action.md` §5.2 still shows `cell_indices: int[][]` — that table is stale and must be corrected (errata task). No handler accepts `cell_indices`.
- **AD-3 — `zone_type` values:** exactly `"growing"` and `"stockpile"` (case-insensitive compare, store lowercase) for M2–M5. `"dumping"` is deferred to the M6 handler backlog. Any other value → `ValidationError`.
- **AD-4 — `IEntityResolver` contract:** does not exist in code yet; UNIT-1 creates it in `RimAI.Agent/Source/Action/EntityResolver.cs` with exactly this shape (typed `Try*` methods, no generics, no exceptions for missing entities):

  ```csharp
  namespace RimAI.Agent.Action
  {
      /// <summary>
      /// Resolves EntityId strings minted by Perception (docs/plan/01-perception.md §9)
      /// into live Verse objects. Main-thread only. Never throws for missing/stale
      /// IDs — returns false with a human-readable error for LLM feedback.
      /// </summary>
      public interface IEntityResolver
      {
          bool TryResolvePawn(string entityId, Verse.Map map, out Verse.Pawn pawn, out string error);
          bool TryResolveThing(string entityId, Verse.Map map, out Verse.Thing thing, out string error);
          bool TryResolveZone(string entityId, Verse.Map map, out Verse.Zone zone, out string error);
          bool TryResolveBill(string entityId, Verse.Map map, out RimWorld.Bill bill, out string error);
          bool TryResolveFaction(string entityId, out RimWorld.Faction faction, out string error);
      }
  }
  ```

  ID prefixes follow the Perception §9 minting contract. Malformed prefix → `error = "Malformed entity id '<id>'"`; well-formed but not found → `error = "<kind> '<id>' not found (despawned or stale snapshot)"`. Both map to `FailureCategory.EntityNotFound` at the handler layer.
- **AD-5 — `ActionCommand.Parameters` type:** stays `Dictionary<string, object>` (non-nullable `object`), matching existing `ActionCommand.cs`. JSON `null` parameter values are treated as **absent** during validation (validators use a shared `TryGetParam<T>` helper that returns false for missing OR null). The spec's `object?` notation is cosmetic; do not enable NRT for this.
- **AD-6 — Unregistered command types:** `CommandDispatcher` routes the 29 not-yet-implemented `ActionCommandType` values to a `StubHandler` that returns `CommandExecutionResult` failure with `FailureCategory.ValidationError` and message `"Command type '<type>' is not yet implemented"`. This is production behavior (G-13), covered by tests — never an exception.
- **AD-7 — Queue persistence:** `MainThreadCommandQueue` is deliberately NOT saved/loaded (no `ExposeData`) per G-16 — stale commands are worse than a skipped beat. Document this in the class XML doc; do not "fix" it.

## Global conventions

- **TDD, always:** every unit lands as failing test → implementation → green test → commit. Tests live in `RimAI.Agent.Tests/` mirroring source folders (`RimAI.Agent.Tests/Action/…`).
- **Threading rule:** anything touching `Verse.*`/`RimWorld.*` live objects runs on the main thread tick. DTOs crossing threads are plain C# — no Verse types leak (same rule Perception already enforces).
- **Testability seam:** handlers and the resolver touch Verse types, so they compile only under net472 against Krafs ref assemblies; pure logic (queue, dispatcher routing, validation, feedback aggregation, serialization) must be testable under net8.0 without a Map. Keep Verse-touching code behind the seam — validation logic takes primitives, not Things.
- **Commits:** one commit per task, conventional-commits style (`feat:`, `fix:`, `test:`, `docs:`), imperative subject. Build + tests green before every commit.
- **Beads:** claim the tracking issue before starting a plan file (`bd update <id> --claim`), close it after the file's validation gate (`bd close <id>`). File new issues for anything discovered but out of scope.
- **JSON:** Newtonsoft.Json, reuse `ColonySnapshotTrimmer.SerializerSettings` where snapshot consistency matters; command fixtures under `RimAI.Agent.Tests/Fixtures/ActionCommands/`.
- **No placeholders:** if you find a "TBD" or a referenced-but-undefined type in a plan file, stop and fix the plan file first (it's a plan bug).

## Current status (2026-07-09)

- **M0 / M0.5:** committed in `9c1e522` (token-usage plumbing); 01-m1-closeout verifies M0.5 acceptance and syncs the stale master-plan checkboxes.
- **M1 Perception:** code-complete and green — 6 source files + 4 test files, all G-02 entity DTOs, EventBus + `Clear()`, 7 Harmony hooks, trimmer, and the dev-mode JSON dump (`SnapshotDebugActions.cs`). 103/103 tests passing. Uncommitted — 01-m1-closeout commits it. In-game snapshot dump verification remains a manual step (needs RimWorld running).
- **M2 Action:** stubs only (`ActionCommand.cs` DTO + enum, `ActionContracts.cs` all `NotImplementedException`). Plan files 02a–02c implement it.
- **M3–M6:** not started; specs in `docs/plan/02`, `04`, `05`.

## Validation gates (per plan file)

Every plan file ends with the same gate, plus file-specific checks listed in the file:

1. `dotnet build RimAI.Agent/RimAI.Agent.csproj` → 0 errors (net472).
2. `dotnet test RimAI.Agent.Tests/RimAI.Agent.Tests.csproj` → 0 failures (both targets).
3. `git status` clean after final commit; tracking issue closed; `bd dolt push` + `git push` done.
