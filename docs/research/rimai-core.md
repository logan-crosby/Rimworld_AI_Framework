# Rimworld_AI_Core — Source Analysis

**Source:** https://github.com/kilo-ki/Rimworld_AI_Core (MIT)
**Commit:** `db419c0`
**License:** MIT — code adoption allowed with attribution
**Analyzed for:** RimAI.Agent (our framework) — gaps G-02, G-23, G-24

---

## 1. Architecture Summary

18-layer modular monolith (P1–P13 phases), single `RimAI.Core` assembly. DI via homegrown `ServiceContainer` (ctor injection, singleton scope, cycle detection). Three `Verse.GameComponent` subclasses: `SchedulerGameComponent` (main-thread pump), `PersistenceManager` (save/load), Harmony-forced `GameComponent` wiring via `Game.FinalizeInit` postfix. Access discipline: Framework only in `LLMService`, Verse reads only in `WorldDataService` + `PersistenceService`, tools only via `IToolRegistryService`. No multi-tier cognition loop — orchestration is single-turn tool dispatch. No game-event capture Harmony hooks beyond UI gizmo injection. Phase-gated: each Pn has a debug-panel Gate for verification. Code is Chinese-commented, English API surface.

```
UI → Orchestration → Tooling → LLMService → RimAIApi (sole consumer)
UI → Stage → History
Tooling → WorldData → Scheduler → main-thread Verse reads
Persistence ↔ file I/O + Scribe (sole I/O surface)
```

---

## 2. Key Patterns

### 2.1 Main-Thread Scheduling (SchedulerService)

All Verse/Unity access marshaled through typed work items. Budget-enforced per frame.

**ISchedulerService** (`RimAI.Core/Source/Infrastructure/Scheduler/ISchedulerService.cs:12`):
```csharp
void ScheduleOnMainThread(Action action, string name, CancellationToken ct);
Task<T> ScheduleOnMainThreadAsync<T>(Func<T> func, string name, CancellationToken ct);
Task ScheduleOnMainThreadAsync(Func<Task> func, string name, CancellationToken ct);
Task DelayOnMainThreadAsync(int ticks, CancellationToken ct);
IDisposable SchedulePeriodic(string name, int everyTicks, Func<CancellationToken,Task> work, ...);
```

**Budget enforcement** (`SchedulerService.cs:184-310`): per-frame `MaxTasksPerUpdate` + `MaxBudgetMsPerUpdate`. Long-task warnings rate-limited (≥250 ticks between warns). Stats snapshotted (`QueueLength`, `LastFrameMs`, `TotalProcessed`).

**SchedulerGameComponent** (`SchedulerGameComponent.cs:13`): `GameComponentTick()` calls `_scheduler.ProcessFrame(tick, ...)`. Also fires Stage triggers every 2500 ticks, kicks off tool-index build at tick 1000, server discovery at tick 1000.

### 2.2 WorldData Facade → Part Delegation

`WorldDataService` is a thin router over 28+ Part classes. Each Part receives `ISchedulerService` + `ConfigurationService`, wraps Verse reads in `ScheduleOnMainThreadAsync` with a per-call timeout CTS.

**ColonyPart** (`RimAI.Core/Source/Modules/World/Parts/ColonyPart.cs:25`):
```csharp
public Task<string> GetPlayerNameAsync(CancellationToken ct = default) {
    var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
    cts.CancelAfter(_cfg.GetWorldDataConfig().DefaultTimeoutMs);
    return _scheduler.ScheduleOnMainThreadAsync(() =>
        Faction.OfPlayer?.Name ?? "Player", name: "GetPlayerName", ct: cts.Token);
}
```

### 2.3 Persistence via GameComponent.ExposeData

**PersistenceManager** (`RimAI.Core/Source/Modules/Persistence/PersistenceManager.cs:10`):
```csharp
internal sealed class PersistenceManager : GameComponent {
    public override void ExposeData() {
        if (Scribe.mode == LoadSaveMode.Saving) {
            var snap = BuildSnapshotFromServices(); // aggregate from memory services
            svc.SaveAll(snap);                      // writes to disk
        } else if (Scribe.mode == LoadSaveMode.LoadingVars) {
            var snap = svc.LoadAll();
            ApplySnapshotToServices(snap);          // hydrate memory services
        }
    }
}
```

**Key insight:** does NOT use RimWorld's Scribe for the actual data — uses its own file-based persistence under `GenFilePaths.SaveDataFolderPath/RimAI/`. Scribe is only used as the trigger (the `ExposeData` call itself). Node-based: each data domain (`ConversationsNode`, `RecapNode`, `BiographiesNode`, etc.) implements `IPersistenceNode { Save(), Load() }`.

**IPersistenceNode** (`RimAI.Core/Source/Modules/Persistence/Parts/IPersistenceNode.cs`):
```csharp
internal interface IPersistenceNode {
    void Save(PersistenceSnapshot snapshot, List<NodeStat> details);
    void Load(PersistenceSnapshot snapshot, List<NodeStat> details);
}
```

Custom Scribe adapters: `Scribe_Dict` (Dictionary<string,T>), `Scribe_Poco` (JSON-serialized POCOs via `LookJsonDict`/`LookJsonList`).

### 2.4 Tool System — JSON Schema + Executor Separation

**IRimAITool** (`RimAI.Core/Source/Modules/Tooling/IRimAITool.cs:3`):
```csharp
internal interface IRimAITool {
    string Name { get; }           // snake_case, e.g. "get_colony_status"
    string Description { get; }    // English, for LLM function descriptions
    string ParametersJson { get; } // JSON Schema string
    string DisplayName { get; }    // localization key: "tool.display.X"
    int Level { get; }             // 1-3 = game, 4 = dev-only
    string BuildToolJson();        // OpenAI function-calling format
}
```

**BuildToolJson** produces OpenAI format (`GetColonyStatusTool.cs:18`):
```csharp
public string BuildToolJson() => JsonConvert.SerializeObject(new {
    type = "function",
    function = new {
        name = Name,
        description = Description,
        parameters = JsonConvert.DeserializeObject<object>(ParametersJson)
    }
});
```

**IToolExecutor** — separate from tool definition (`Execution/IToolExecutor.cs:7`):
```csharp
internal interface IToolExecutor {
    string Name { get; }
    Task<object> ExecuteAsync(Dictionary<string, object> args, CancellationToken ct);
}
```

**Two selection modes** (`ToolRegistryService`):
- **Classic**: returns all tools (filtered by Level, research gate, whitelist/blacklist)
- **NarrowTopK**: embedding-based semantic search — embeds user input, cosine-similarity ranks tool descriptions, returns top-K. Index persisted to disk, auto-rebuilt on config change.

**Level gating**: `BuildToolsAsync` filters `t.Level <= maxLevel` where maxLevel is computed from participant AI-server levels (1-3). Research gating via `IResearchGatedTool.RequiredResearchDefNames`.

### 2.5 RimAIApi Consumption Pattern

`LLMService` is the SOLE consumer — no other class touches `RimAI.Framework.API`. Static calls all go through retry + circuit-breaker wrapper:

**LLMService** (`RimAI.Core/Source/Modules/LLM/LLMService.cs:87`):
```csharp
private Task<Result<UnifiedChatResponse>> CallWithRetryAsync(
    string circuitKeySuffix,
    Func<CancellationToken, Task<Result<UnifiedChatResponse>>> action,
    CancellationToken cancellationToken)
{
    // 1. Check circuit breaker (LlmPolicies.IsAllowedByCircuit)
    // 2. Execute with retry (LlmPolicies.ExecuteWithRetryAsync, maxAttempts, exp backoff)
    // 3. Record success/failure for circuit state
}
```

API calls used: `GetCompletionAsync()`, `GetCompletionWithToolsAsync()`, `StreamCompletionAsync()`, `GetEmbeddingsAsync()`, `InvalidateConversationCacheAsync()`. Streaming has heartbeat timeout — resets `CancelAfter` on each chunk received. Config hot-reload: retry/circuit/timeout params update live.

### 2.6 Orchestration — Simple Tool Dispatch

**OrchestrationService** (`RimAI.Core/Source/Modules/Orchestration/OrchestrationService.cs:17`):
```
1. Compute max tool level from participant AI-server levels
2. Select tools via Classic or NarrowTopK mode
3. Construct system+history+user messages
4. Single non-streaming LLM call with tools
5. Execute decided tool calls serially (max MaxCalls, default 1)
6. Return ToolCallsResult with plan trace + execution records
```

**NOT a cognition loop** — no Reactive/Tactical/Strategic tiers, no cadence, no budgeting, no autonomy levels. This is a user-initiated chat command dispatcher, not autonomous AI.

### 2.7 Harmony Patches — Minimal

Only 2 patches (`HarmonyPatcher.cs`):
| Target | Type | Purpose |
|--------|------|---------|
| `Pawn.GetGizmos` | Postfix | Add "Info Send" gizmo → opens ChatWindow |
| `Game.FinalizeInit` | Postfix | Ensure `SchedulerGameComponent` + `PersistenceManager` exist |

**No game event capture** — no Letter, Pawn.Kill, Fire, MentalBreak, or any reactive hooks. Stage triggers are timer-based (`GlobalTimedRandomActTrigger`), not event-driven.

---

## 3. Gap Question Answers

### G-02 — Snapshot entity coverage

**What they have:** `WorldDataService` exposes ~40 async query methods (ColonySnapshot, FoodInventory, ThreatSnapshot, PawnHealth, etc.) covering most of our v2 DTO needs. Their snapshots are **on-demand, point queries**, not a unified colony-wide snapshot. They have `ColonySnapshot` (name, colonist list with name/age/gender/job), `ResourceOverviewSnapshot`, `BuildingPowerPart`, `FactionPart` — covering Buildings, Power, Factions territory.

**What they lack:** Their snapshots are reactive to tool calls, not pre-built for LLM context. No unified `ColonySnapshot` DTO that includes ALL subsystems at once. No ID minting scheme for cross-referencing (no `EntityId` equivalent). No trimmer — raw JSON goes to LLM unfiltered.

**Our better approach:** Pre-built unified snapshot with `EntityId` minting + trimmer is correct. Their Part-per-domain pattern IS worth adopting for the *implementation* of our snapshot builder.

### G-23 — EventBus static lifecycle

**What they do:** `PersistenceManager` is a `GameComponent` — RimWorld handles its lifecycle (created on game start, `ExposeData` on save/load, destroyed on return-to-menu). No static event bus. Static state lives in the DI container (`RimAICoreMod.Container` is a static field). No `Clear()` equivalent — but no static sinks either.

**Our risk:** Their `ServiceContainer._singletons` survives return-to-menu because `RimAICoreMod` is a `Mod` (singleton). Services re-resolve on new game. If we use a static `EventBus` with subscriber list, we NEED `Clear()` + re-subscribe in `FinalizeInit()` — they don't have this problem because they have no event bus.

### G-24 — Map selection policy

**What they do:** `ColonyPart.GetAllColonistLoadIdsAsync` iterates `Find.Maps` (all maps), filters by `FreeColonists`. `GetColonySnapshotAsync` also iterates `Find.Maps`. Most Parts use `Find.CurrentMap` implicitly (e.g., `GetBeautyAverageAsync` takes explicit x,z coords — doesn't specify which map). **No consistent map selection policy** — they don't need one because their chat UI is pawn-contextual (selected pawn determines the map).

**Our need:** We need `Find.AnyPlayerHomeMap` consistently, as spec'd. Their approach of pawn-contextual map resolution works for chat but NOT for autonomous colony management.

---

## 4. ADOPT vs AVOID

### ADOPT (lift these patterns)

| Pattern | Source | Why |
|---------|--------|-----|
| `ScheduleOnMainThreadAsync<T>(Func<T>)` + per-call timeout CTS | SchedulerService + ColonyPart | Clean, safe main-thread marshaling. Every Part method follows this template. |
| WorldData facade → Part delegation (thin router) | WorldDataService + 28 Parts | Single-responsibility Parts, facade stays readable. Each Part self-contained with its own DTOs. |
| `PersistenceManager : GameComponent` for save/load hook | PersistenceManager | Clean `ExposeData()` integration. Separate `PersistenceService` for I/O. |
| `IPersistenceNode` per data domain | PersistenceNode interface + 12 nodes | Each domain (Conversations, Biographies, Recap, etc.) owns its save/load logic. Easy to add new domains. |
| `IRimAITool` + `IToolExecutor` separation | Tooling module | Tool definition (JSON Schema) decoupled from execution. Clean testability — mock executor, verify schema. |
| `Level` gating on tools (1-3 game, 4 dev) | IRimAITool.Level + BuildToolsAsync | Prevents dev/debug tools leaking to LLM. Research gating via `IResearchGatedTool`. |
| `BuildToolJson()` producing OpenAI format | GetColonyStatusTool | One canonical JSON format — all tools consistent. |
| Single `RimAIApi` consumer (LLMService) | LLMService | Framework access in one place. Easier to mock, retry, circuit-break. |
| Retry + circuit breaker for LLM calls | LlmPolicies + CallWithRetryAsync | Production-grade resilience. Configurable per env. |
| Streaming heartbeat timeout | StreamResponseAsync heartbeat watchdog | Prevents hung streams. Resets timer per chunk. |

### AVOID (their mistakes / limits)

| Issue | Where | Why |
|-------|-------|-----|
| No game event capture via Harmony | HarmonyPatcher (only 2 patches) | No reactive loop possible — can't respond to raids, deaths, fires. We need our 7 patches from 01 §4.3. |
| No multi-tier cognition loop | OrchestrationService (single-turn tool dispatch) | No Reactive/Tactical/Strategic cadence. No autonomous planning. We need our full 05 spec. |
| No token/cost budget management | (absent) | No tracking, no degradation. We need our `IBudgetManager` from 05 §4. |
| No safety guardrails / autonomy levels | (absent) | All tool calls execute unconditionally. We need our `ISafetyGuardrail` from 05 §5. |
| No snapshot diffing / event-driven triggers | (absent) | Snapshot is point-query, not periodic diff. Stage triggers are timer-based. We need our `SnapshotDiffEngine` + `EventBus`. |
| Service Locator anti-pattern | ExecutorDiscovery, ColonyStatusExecutor, PersistenceManager | `RimAICoreMod.Container.Resolve<T>()` scattered everywhere — hard to test, hidden deps. We should use pure ctor injection. |
| Homegrown DI container | ServiceContainer (160 lines, no scoping) | Works for singletons but no lifetime management. We should use RimWorld's built-in `GameComponent` lifecycle or a standard DI lib. |
| File-based persistence (not RimWorld Scribe) | PersistenceService saves to `SaveDataFolderPath/RimAI/` | Bypasses RimWorld's save system — won't survive Steam Cloud sync, mod version migration. We should use Scribe directly in `ExposeData`. |
| `Find.Maps` iteration (no consistent map policy) | ColonyPart, scattered Parts | Multi-colony ambiguity. We spec `Find.AnyPlayerHomeMap`. |
| No ID minting scheme | (absent) | pawnLoadId used ad-hoc as `thingIDNumber`. No zone/bill/faction ID convention. We need our `EntityId` from G-03. |
| Chinese-language codebase | All Source/ | Variable names, comments, log messages in Chinese. Our codebase is English. |
| No test project | (absent) | Zero tests in repo. We already have `RimAI.Agent.Tests` (G-06). |

---

## 5. RimAIApi Usage — Patterns Worth Copying

The Framework we forked (`RimAI.Framework`) provides `RimAIApi` as a static facade. Their `LLMService` wraps it well:

```csharp
// Static call pattern (LLMService.cs:87)
RimAIApi.GetCompletionAsync(request, ct)                    // non-streaming
RimAIApi.GetCompletionWithToolsAsync(messages, tools, cid, ct) // function-calling
RimAIApi.StreamCompletionAsync(request, ct)                 // SSE streaming
RimAIApi.GetEmbeddingsAsync(request, ct)                    // embeddings
RimAIApi.InvalidateConversationCacheAsync(cid, ct)          // cache bust
RimAIApi.IsEmbeddingEnabled()                               // feature flag
```

Their `ToolDefinition` wrapper converts our `IRimAITool.BuildToolJson()` output (JSON string) into `RimAI.Framework.Contracts.ToolDefinition` objects before calling `GetCompletionWithToolsAsync`. The conversion is robust — handles case-insensitive JSON key matching, defaults missing `parameters` to `{type:"object", properties:{}, required:[]}`.

**Key takeaway:** Their `LLMService` is the right shape for our `ILlmClient` interface (G-07). We should wrap `RimAIApi` behind `ILlmClient` with the same retry/circuit/heartbeat patterns.
