# 05 -- Orchestration Layer Spec

**Status:** authoritative implementation spec for M3-M6
**Namespace:** `RimAI.Agent.Orchestration`
**Depends on:** Perception (01), Cognition (02), Action (03), Memory (04), RimAI.Framework
**Start from:** `RimAI.Agent/Source/Orchestration/` skeleton stubs (`// TODO(impl)` contracts)

---

## 1. Purpose & Architecture

The Orchestrator is the `Verse.GameComponent` that drives the entire AI loop, coordinating Perception → Cognition → Action → Memory through tick-based scheduling. It enforces token/cost budgets, applies safety guardrails per autonomy level, and logs all decisions.

```
  Game Tick ──► AgentGameComponent
                 1. Check EventBus for reactive triggers
                 2. Process pending ActionCommands
                 3. Evaluate cadence timers
                 4. Launch async planners (fire-and-forget)
                 5. Feed results into Memory + Decision Log

                 ┌──────────┬──────────┬──────────┐
                 │ Cadence  │ Budget   │ Safety   │ Decis.Log │
                 └──────────┴──────────┴──────────┘
                         │ async (Task.Run)
         ┌───────────────┼───────────────┐
         ▼               ▼               ▼
  ReactivePlanner  TacticalPlanner  StrategicPlanner
```

**Threading rule:** GameComponent ticks run on Unity main thread. LLM calls are async off-thread via `Task.Run`. Command execution marshals back to main thread via an `ActionCommand` queue. No game state mutation outside the main-thread tick handler.
**Save compatibility:** All persistent state lives in `AgentGameComponent.ExposeData()`. Mod loads into saves that never had it and unloads cleanly.

---

## 2. GameComponent Tick Driver

`AgentGameComponent : Verse.GameComponent`

| Override | Responsibility |
|---|---|
| `GameComponentUpdate()` | Primary tick loop -- five phases executed sequentially |
| `ExposeData()` | Serialize/deserialize cadence state, budget state, decision log ring buffer |
| `FinalizeInit()` | Wire up EventBus subscriptions, restore cadence from deserialized state |

**Tick phases** (each `GameComponentUpdate` call):

1. **Drain Events** -- Dequeue `PerceptionEvent`s from EventBus; filter `CriticalLevel >= Medium` for reactive.
2. **Process Commands** -- Call `IActionExecutor.ExecutePendingCommands()` to drain the marshalled queue.
3. **Evaluate Cadence** -- Check `ShouldFireReactive/Tactical/Strategic()`.
4. **Launch Planners** -- For each due tier where `CanAfford(tier, estimate)`, fire `Task.Run(() => cognition.PlanXxxAsync(...))` with a `CancellationToken`.
5. **Feed Results** -- Each planner's continuation (marshalled to main thread via `LongEventHandler.ExecuteWhenFinished`) pushes `ActionCommand[]` to queue, logs via `IDecisionLogger`, deducts via `IBudgetManager`.

**Main-thread safety:** Phases 1-3 run synchronously on main thread (all game state access). Phase 4 uses DTOs only (no Verse types). Phase 5 continuations are posted back to main thread. Per-flight `CancellationTokenSource` stored; all cancelled on pause/mod-unload.

---

## 3. Cadence Scheduler

`ICadenceScheduler` controls when each cognition tier fires.

| Tier | Trigger | Game-time cadence | Tick equivalent |
|---|---|---|---|
| Reactive | `EventBus.Publish(PerceptionEvent)`, `CriticalLevel >= Medium` | <1 game-hour | Next tick |
| Tactical | Scheduled timer | ~every 6 game-hours | 60,000 ticks |
| Strategic | Scheduled timer | ~every 2 game-days | 120,000 ticks |

**Cooldown:** `CadenceState` DTO tracks `LastReactiveTick`, `LastTacticalTick`, `LastStrategicTick`, `MinimumSpacingTicks = 5000` (~0.5 game-hours). Each `ShouldFire*()` checks its tier's cooldown PLUS the minimum spacing against all tiers to prevent cognition storming. If reactive and tactical are both due on the same tick, reactive wins priority.

**Force-trigger API:** `ForceTriggerReactive(GameAlert)`, `ForceTriggerTactical()`, `ForceTriggerStrategic()`, `ResetAllCooldowns()`.

---

## 4. Token/Cost Budget Manager

`IBudgetManager` enforces per-day token and API call budgets with tier degradation.

```csharp
public class BudgetConfig {
    public int DailyTokenBudget = 200_000;
    public int DailyCallBudget = 100;
    public float ReactiveReserve = 0.10f;    // 10% of budget
    public float TacticalReserve = 0.40f;     // 40% of budget
    public float StrategicReserve = 0.50f;    // 50% of budget
}
```

**Degradation order** (Strategic deactivates first, Reactive survives longest):

| Remaining budget | Strategic | Tactical | Reactive |
|---|---|---|---|
| 100% - 21% | Active | Active | Active |
| 20% - 6% | Deactivated | Active | Active |
| 5% - 1% | Deactivated | Deactivated | Active |
| 0% | Deactivated | Deactivated | Until fully exhausted |

**BudgetState DTO:** `TokensUsedToday`, `CallsUsedToday`, `LastResetDay`, `ActiveTiers` (HashSet).

**Interface:** `CanAfford(tier, estimatedTokens) → bool`, `Deduct(tier, actualTokens)`, `GetState()`, `ApplyConfig(config)`, `ResetDaily()`. Daily reset triggers at midnight game-time (`gameDay >= LastResetDay + 1`), resets counters and re-evaluates active tiers. **Overage grace:** an in-flight call that exceeds remaining budget completes and charges the overage -- no mid-flight kill.

---

## 5. Safety Guardrails

`ISafetyGuardrail` controls command execution per autonomy level.

```csharp
public enum AutonomyLevel { Advisor, Copilot, Full }
```

| Classification | Examples | Advisor | Copilot | Full |
|---|---|---|---|---|
| **Destructive** | Attack, Draft, Arrest, Deconstruct, Banish, Abandon, Caravan | Blocked | Confirm dialog | Execute |
| **Safe** | SetWorkPriority, AddBill, CreateZone, SetPolicy, Equip, SetMedicine | Suggest only | Auto-execute | Execute |
| **Neutral** | Research, BuildDesignation, Trade, SetAllowedArea, CancelBill | Suggest only | Auto-execute | Execute |

**Confirmation dialog:** Copilot mode + destructive command. Modal pauses game, shows command + reason. 30s real-time timeout; default = Reject. On Full→Copilot transition: flush all pending destructive commands from queue.

**Interface:** `AllowsCommand(ActionCommand)`, `AllowsAdvancedAction(ActionCommandType)`, `SetAutonomyLevel(level)`, `CurrentLevel`, `RequiresConfirmation(type)`.

---

## 6. Decision Log

`IDecisionLogger` records every AI-generated command.

```csharp
public class DecisionLogEntry {
    public int Tick;
    public float GameTime;        // game-days
    public CognitionTier Tier;
    public ActionCommandType CommandType;
    public string Parameters;     // truncated JSON, max 200 chars
    public string Reason;         // planner reasoning, max 200 chars
    public bool Success;          // set after execution
    public int TokensUsed;        // set after LLM call
}
```

**Interface:** `Log(entry)`, `GetRecentDecisions(count, tier?)`, `GetDecisionsSince(tick)`, `ExportToFile(path)`.

**Ring buffer:** 500 entries (configurable), oldest evicted silently, persisted via `ExposeData`. Export via dev console: `RimAI.DumpDecisionLog` writes last 200 entries to a text file.

---

## 7. UI Control Panel Spec

Accessed via `Mod.SettingsCategory` in RimWorld mod settings.

| Control | Type | Range | Default |
|---|---|---|---|
| AI Enable/Disable | Toggle (confirmation) | On/Off | On |
| Autonomy Level | Dropdown | Advisor / Copilot / Full | Copilot |
| Daily Token Budget | Slider | 10K -- 1M (step 5K) | 200K |
| Daily Call Budget | Slider | 10 -- 500 (step 5) | 100 |
| Strategic Reserve % | Slider | 10 -- 70 | 50 |
| Tactical Reserve % | Slider | 10 -- 60 | 40 |
| Reactive Reserve % | Slider | 5 -- 30 | 10 |
| Decision Log Size | Slider | 100 -- 2000 | 500 |

**Current budget state display** (read-only): tokens used / limit, calls used / limit, current game-day, active tiers with paused-degraded rationale.

**Decision log viewer:** scrollable list newest-first, filterable by tier dropdown (All/Reactive/Tactical/Strategic). Green = success, red = rejected/failed. "Export to File" button.

**Pause AI button:** cancels in-flight Tasks, clears pending commands, preserves all state. Resume restores cadence from save -- no retroactive catch-up.

---

## 8. Interfaces & DTO Definitions

```csharp
namespace RimAI.Agent.Orchestration
{
    public interface IOrchestrator {
        void Start(); void Pause(); void Resume(); void Shutdown();
        bool IsRunning { get; }
        AutonomyLevel AutonomyLevel { get; set; }
        BudgetConfig Budget { get; set; }
        BudgetState BudgetState { get; }
        IReadOnlyList<DecisionLogEntry> RecentDecisions(int count, CognitionTier? tier);
        void ForceTrigger(CognitionTier tier, GameAlert? alert = null);
    }

    public interface ICadenceScheduler {
        bool ShouldFireReactive(); bool ShouldFireTactical(); bool ShouldFireStrategic();
        void MarkFired(CognitionTier tier);
        void ForceTriggerReactive(GameAlert alert); void ForceTriggerTactical(); void ForceTriggerStrategic();
        void ResetAllCooldowns();
        CadenceState GetState();
    }

    public interface IBudgetManager {
        bool CanAfford(CognitionTier tier, int estimatedTokens);
        void Deduct(CognitionTier tier, int actualTokens);
        BudgetState GetState();
        void ApplyConfig(BudgetConfig config);
        void ResetDaily();
    }

    public interface ISafetyGuardrail {
        bool AllowsCommand(ActionCommand command);
        bool AllowsAdvancedAction(ActionCommandType type);
        void SetAutonomyLevel(AutonomyLevel level);
        AutonomyLevel CurrentLevel { get; }
        bool RequiresConfirmation(ActionCommandType type);
    }

    public interface IDecisionLogger {
        void Log(DecisionLogEntry entry);
        IReadOnlyList<DecisionLogEntry> GetRecentDecisions(int count = 20, CognitionTier? tier = null);
        IReadOnlyList<DecisionLogEntry> GetDecisionsSince(int tick);
        void ExportToFile(string path);
    }
}
```

**DTOs:** `AutonomyLevel` enum {Advisor, Copilot, Full}. `BudgetConfig` {DailyTokenBudget=200K, DailyCallBudget=100, ReactiveReserve=0.10f, TacticalReserve=0.40f, StrategicReserve=0.50f}. `BudgetState` {TokensUsedToday, CallsUsedToday, LastResetDay, ActiveTiers:HashSet}. `CadenceState` {LastReactiveTick, LastTacticalTick, LastStrategicTick, MinimumSpacingTicks=5000}. `DecisionLogEntry` {Tick, GameTime, Tier, CommandType, Parameters, Reason, Success, TokensUsed}.

---

## 9. Acceptance Criteria

| Milestone | Criteria |
|---|---|
| **M3** | Reactive loop: EventBus triggers on raid alert. Copilot confirms drafting; safe commands auto-execute. Budget unlimited. |
| **M4** | Tactical fires on schedule (~6h). Budget tracks tokens. Colony runs 5 game-days without starvation. Reactive independent. |
| **M5** | Strategic fires (~2d). Degradation: Strategic drops at 20%. Memory feeds strategic prompts. Save/load preserves cadence+budget. |
| **M6** | Full autonomy unattended. Decision log viewer works. All UI controls functional. Playthrough landing→stable colony. |

---

## 10. Edge Cases

| Scenario | Behavior |
|---|---|
| **Save/load mid-thought** | Cancel in-flight Tasks. Deserialize cadence from ExposeData; re-evaluate next tick. Queued commands preserved. |
| **Pause/resume AI** | Pause cancels Tasks + clears queue, preserves state. Resume restores cadence; no retroactive catch-up. |
| **Mod unload** | Cancel all Tasks, dispose CancellationTokenSources, null static refs. |
| **Budget exhaustion** | All tiers deactivate. On-screen notification "RimAI daily budget exhausted until midnight (Day X)." Reactive active until tokens AND calls both hit 0. |
| **Time scale changes** | Cadence uses game ticks, not wall-clock. Speed changes have no effect. |
| **Reactive + scheduled collision** | Minimum spacing prevents same-tick. Reactive wins priority if both due. |
| **Config changes mid-day** | Reserves update immediately; TokensUsedToday not retroactive. Degradation re-evaluates on next CanAfford(). |
| **UI confirmation timeout** | 30s real-time, default reject. Game stays paused; dialog shows countdown. |
