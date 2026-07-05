# 03 — Action Layer Spec

**Status:** draft | **Implements:** RimAI.Agent.Action
**Amendments (06-plan-audit.md wins):** whitelist gains construction family (`PlaceBlueprint`, `PlaceFloor`, `CancelBlueprint`, `Deconstruct`) + `SetPlantToGrow` + `SetPrisonerInteraction` — post-audit 41 types (G-04, G-12). Handlers are **phased** — M2 ships the core-12 only; unregistered types return `ValidationError("not yet implemented")`, never throw (G-13). Entity IDs follow the shared `EntityId` rule and `IEntityResolver` (G-03). `CreateZone` takes rect params `x,z,width,height` (max 20×20), not raw cell arrays (G-21). `AddDesignation.designation_type` enum: `Hunt, Tame, Slaughter, Mine, CutPlants, Harvest, HaulThing, Strip, Open` (G-04). Commands carry optional `SnapshotTick`; reject if source snapshot >15,000 ticks old (G-17).
**Prerequisite specs:** 00-MASTER-PLAN.md, 01-perception.md, 02-cognition.md
**Consumed by:** 05-orchestration.md

## 1. Purpose

The Action layer is the **only code path that mutates game state**. It receives validated
`ActionCommand` DTOs from Cognition, executes them against RimWorld APIs on the main thread,
verifies the result, and reports success/failure back. It enforces a strict whitelist —
no LLM-generated command is executed without schema validation and run-time safety checks.

## 2. Architecture

```
Cognition ──► ActionCommand[] (validated DTOs)
                    │
                    ▼
         ┌─────────────────────────────┐
         │    MainThreadCommandQueue    │  ← thread-safe, drained on Unity main thread
         └─────────────┬───────────────┘
                       ▼
         ┌─────────────────────────────┐
         │   CommandDispatcher          │  ← routes ActionCommandType → ICommandHandler
         └─────────────┬───────────────┘
                       ▼
         ┌─────────────────────────────┐
         │   ICommandHandler[]          │  ← one handler per command type
         │   (SetWorkPriorityHandler,   │
         │    DraftHandler, etc.)       │
         └─────────────┬───────────────┘
                       ▼
         ┌─────────────────────────────┐
         │   PostExecutionVerifier      │  ← confirms game state changed as expected
         └─────────────┬───────────────┘
                       ▼
         ┌─────────────────────────────┐
         │   ActionFeedback             │  ← reported back to Cognition/Memory
         └─────────────────────────────┘
```

## 3. Namespace & Entry Point

```csharp
namespace RimAI.Agent.Action
{
    /// <summary>
    /// Public entry point for the Action layer.
    /// All game mutation flows through this interface on the main thread.
    /// </summary>
    public interface IActionExecutor
    {
        /// <summary>
        /// Enqueue commands for execution on the next main-thread tick.
        /// Thread-safe — callable from any thread (Cognition runs off-thread).
        /// </summary>
        void EnqueueCommands(IReadOnlyList<ActionCommand> commands);

        /// <summary>
        /// Drain and execute all queued commands. MUST be called from the
        /// Unity main thread (Orchestrator's GameComponent tick).
        /// Returns per-command results for feedback loop.
        /// </summary>
        CommandResult[] ExecutePendingCommands();

        /// <summary>
        /// Number of commands currently queued awaiting execution.
        /// </summary>
        int PendingCount { get; }

        /// <summary>
        /// Maximum queue depth before EnqueueCommands drops overflow
        /// (with logged warning).
        /// </summary>
        int MaxQueueDepth { get; set; }  // default: 100
    }

    /// <summary>
    /// Concrete implementation. Owns the queue, dispatcher, and verifier.
    /// Constructed by Orchestrator; lives for the game session.
    /// </summary>
    public class ActionExecutor : IActionExecutor
    {
        // constructor: ICommandDispatcher dispatcher, IPostExecutionVerifier verifier,
        //              IActionLogger logger, int maxQueueDepth = 100

        // TODO(impl): EnqueueCommands, ExecutePendingCommands
    }
}
```

## 4. ActionCommand DTO

```csharp
namespace RimAI.Agent.Action
{
    /// <summary>
    /// The universal DTO for all game commands. Serialized by Cognition,
    /// deserialized and executed by Action. No Verse/RimWorld types in this DTO.
    /// All entity references use string IDs from the Perception snapshot.
    /// </summary>
    [Serializable]
    public class ActionCommand
    {
        /// <summary>Unique ID for traceability across layers.</summary>
        public string CommandId { get; init; } = Guid.NewGuid().ToString("N");

        /// <summary>Whitelisted command type.</summary>
        public ActionCommandType Type { get; init; }

        /// <summary>Which planner tier originated this command.</summary>
        public string SourceTier { get; init; } = "unknown";  // "reactive", "tactical", "strategic"

        /// <summary>ConversationId from the Cognition session that produced this.</summary>
        public string ConversationId { get; init; } = "";

        /// <summary>UTC timestamp when the command was generated.</summary>
        public DateTime GeneratedAt { get; init; } = DateTime.UtcNow;

        /// <summary>Priority for queue ordering (0 = highest).</summary>
        public int Priority { get; init; } = 2;  // 0=critical(rescue/draft), 1=urgent, 2=normal, 3=low

        /// <summary>Command-type-specific parameters. Schema varies by Type.</summary>
        public Dictionary<string, object?> Parameters { get; init; } = new();

        /// <summary>Optional human-readable reason for debugging/decision log.</summary>
        public string Reason { get; init; } = "";
    }

    /// <summary>
    /// Exhaustive whitelist. Every value maps to a registered ICommandHandler.
    /// Adding a value here requires: handler impl, tool schema update, validator update.
    /// </summary>
    public enum ActionCommandType
    {
        // --- Work & Bills ---
        SetWorkPriority,
        AddBill,
        RemoveBill,
        SuspendBill,
        ResumeBill,

        // --- Zones ---
        CreateZone,
        DeleteZone,
        AssignToZone,
        UnassignFromZone,
        RenameZone,

        // --- Designations ---
        AddDesignation,
        RemoveDesignation,

        // --- Drafting & Combat ---
        DraftColonist,
        UndraftColonist,
        MoveTo,
        AttackTarget,
        HoldFire,
        FireAtWill,

        // --- Medical & Rescue ---
        RescueColonist,
        CapturePrisoner,
        TendPawn,
        PrioritizeSurgery,
        SetMedicalDefaults,

        // --- Equipment & Loadout ---
        EquipItem,
        DropItem,
        SetOutfitPolicy,
        SetDrugPolicy,
        SetFoodPolicy,

        // --- Research ---
        SetResearchProject,

        // --- Trade & Caravans ---
        InitiateTrade,
        FormCaravan,
        CancelCommand,

        // --- Schedule ---
        SetSchedule,
        SetRecreation,
    }
}
```

## 5. Command Parameters Schema (per type)

Each `ActionCommand` carries a `Parameters` dictionary. The expected keys per type:

### 5.1 Work & Bills

| CommandType | Required Keys | Optional Keys | Value Types |
|---|---|---|---|
| `SetWorkPriority` | `colonist_id`, `work_type`, `priority` | — | string, string, int(0-4) |
| `AddBill` | `production_table_id`, `recipe_def_name`, `count` | `ingredient_radius`, `worker_skill_min`, `repeat_mode` | string, string, int, float?, int?, string |
| `RemoveBill` | `bill_id` | — | string |
| `SuspendBill` | `bill_id` | — | string |
| `ResumeBill` | `bill_id` | — | string |

### 5.2 Zones

| CommandType | Required Keys | Optional Keys | Value Types |
|---|---|---|---|
| `CreateZone` | `zone_type`, `cell_indices` | `label` | string, int[][], string? |
| `DeleteZone` | `zone_id` | — | string |
| `AssignToZone` | `colonist_id`, `zone_id` | — | string, string |
| `UnassignFromZone` | `colonist_id`, `zone_id` | — | string, string |
| `RenameZone` | `zone_id`, `new_label` | — | string, string |

### 5.3 Designations

| CommandType | Required Keys | Optional Keys | Value Types |
|---|---|---|---|
| `AddDesignation` | `designation_type`, `target_ids` | — | string, string[] |
| `RemoveDesignation` | `designation_id` or `target_ids` | `designation_type` | string/string[], string? |

### 5.4 Drafting & Combat

| CommandType | Required Keys | Optional Keys | Value Types |
|---|---|---|---|
| `DraftColonist` | `colonist_id` | — | string |
| `UndraftColonist` | `colonist_id` | — | string |
| `MoveTo` | `colonist_id`, `cell_x`, `cell_z` | `map_id` | string, int, int, string? |
| `AttackTarget` | `colonist_id`, `target_id` | `use_ranged` | string, string, bool |
| `HoldFire` | `colonist_id` | — | string |
| `FireAtWill` | `colonist_id` | — | string |

### 5.5 Medical & Rescue

| CommandType | Required Keys | Optional Keys | Value Types |
|---|---|---|---|
| `RescueColonist` | `rescuer_id`, `victim_id` | `bed_id` | string, string, string? |
| `CapturePrisoner` | `captor_id`, `target_id` | `bed_id` | string, string, string? |
| `TendPawn` | `doctor_id`, `patient_id` | `use_best_medicine` | string, string, bool |
| `PrioritizeSurgery` | `patient_id`, `surgery_bill_id` | — | string, string |
| `SetMedicalDefaults` | `colonist_id` | `medicine_policy`, `max_medicine_level` | string, string?, string? |

### 5.6 Equipment & Loadout

| CommandType | Required Keys | Optional Keys | Value Types |
|---|---|---|---|
| `EquipItem` | `colonist_id`, `item_id_or_def_name` | — | string, string |
| `DropItem` | `colonist_id`, `item_id` | `drop_on_ground` | string, string, bool |
| `SetOutfitPolicy` | `colonist_id`, `outfit_name` | — | string, string |
| `SetDrugPolicy` | `colonist_id`, `drug_policy_name` | — | string, string |
| `SetFoodPolicy` | `colonist_id`, `food_restriction_name` | — | string, string |

### 5.7 Research, Trade, Schedule

| CommandType | Required Keys | Optional Keys | Value Types |
|---|---|---|---|
| `SetResearchProject` | `project_def_name` | `priority` | string, int(0-1) |
| `InitiateTrade` | `settlement_id_or_faction` | `trader_colonist_id` | string, string? |
| `FormCaravan` | `colonist_ids`, `destination_tile`, `departure_direction` | `pack_animals`, `supply_days` | string[], int, string, string[]?, float? |
| `CancelCommand` | `command_id` | — | string |
| `SetSchedule` | `colonist_id`, `hour_map` | — | string, int[] (length 24) |
| `SetRecreation` | `colonist_id` | `schedule_hours` | string, int[]? |

## 6. Command Handler Interface

```csharp
namespace RimAI.Agent.Action
{
    /// <summary>
    /// One handler per ActionCommandType. Executes on the main thread
    /// with full access to RimWorld APIs. Must be registered in CommandDispatcher.
    /// </summary>
    public interface ICommandHandler
    {
        /// <summary>The command type this handler processes.</summary>
        ActionCommandType HandledType { get; }

        /// <summary>
        /// Execute the command against RimWorld game state.
        /// All Verse/Map/Pawn lookups happen here — nowhere else in the pipeline.
        /// </summary>
        /// <param name="command">The validated ActionCommand to execute.</param>
        /// <param name="map">The current colony map (from Find.CurrentMap).</param>
        /// <returns>ExecutionResult with success/failure and verification hints.</returns>
        CommandExecutionResult Execute(ActionCommand command, Verse.Map map);

        /// <summary>
        /// Validate that the command's Parameters are sufficient and target
        /// entities still exist in the current game state. Called before Execute.
        /// Returns null if valid, error string if not.
        /// </summary>
        string? Validate(ActionCommand command);
    }

    public class CommandExecutionResult
    {
        public string CommandId { get; init; } = "";
        public ActionCommandType Type { get; init; }
        public bool IsSuccess { get; init; }
        public string? ErrorMessage { get; init; }
        /// <summary>Key-value pairs describing expected post-execution state
        /// (e.g. {"colonist_123_drafted": "true", "position": "45,32"}).
        /// Used by PostExecutionVerifier.</summary>
        public Dictionary<string, string> VerificationHints { get; init; } = new();
    }

    /// <summary>
    /// Routes ActionCommand to the correct ICommandHandler by Type.
    /// </summary>
    public interface ICommandDispatcher
    {
        /// <summary>Register a handler for its HandledType.</summary>
        void RegisterHandler(ICommandHandler handler);

        /// <summary>Dispatch a command to its handler; throws if no handler registered.</summary>
        CommandExecutionResult Dispatch(ActionCommand command, Verse.Map map);
    }
}
```

## 7. Main-Thread Command Queue

```csharp
namespace RimAI.Agent.Action
{
    /// <summary>
    /// Thread-safe queue between Cognition (off-thread) and Action (main thread).
    /// Uses lock-free ConcurrentQueue internally for enqueue; drain is single-threaded.
    /// </summary>
    public class MainThreadCommandQueue
    {
        private readonly ConcurrentQueue<ActionCommand> _queue = new();
        private volatile int _count;

        /// <summary>Thread-safe enqueue. Drops commands exceeding MaxDepth.</summary>
        public void Enqueue(ActionCommand command, int maxDepth = 100);

        /// <summary>Bulk enqueue, ordered by Priority (ascending).</summary>
        public void EnqueueBatch(IReadOnlyList<ActionCommand> commands, int maxDepth = 100);

        /// <summary>Drain all commands sorted by priority, returning oldest-first within
        /// same priority. MUST be called on main thread.</summary>
        public ActionCommand[] DrainAll();

        public int Count => _count;
    }
}
```

**Queue ordering:** Commands are sorted by `Priority` (0=critical first), then by `GeneratedAt`
(FIFO within same priority). Critical priority is reserved for life-saving commands
(`RescueColonist`, `DraftColonist`, `TendPawn`, `CapturePrisoner`).

## 8. Post-Execution Verification

```csharp
namespace RimAI.Agent.Action
{
    /// <summary>
    /// After each command executes, verifies that the intended game state change
    /// actually occurred. Catches cases where RimWorld silently rejects a command
    /// (path unreachable, target already dead, bill already completed, etc.).
    /// </summary>
    public interface IPostExecutionVerifier
    {
        /// <summary>
        /// Verify a single command's execution using its VerificationHints.
        /// Must be called on the main thread (reads game state).
        /// </summary>
        VerificationResult Verify(CommandExecutionResult executionResult, Verse.Map map);
    }

    public class VerificationResult
    {
        public string CommandId { get; init; } = "";
        public bool Verified { get; init; }
        public bool ExpectedStateConfirmed { get; init; }
        public string[] UnconfirmedHints { get; init; } = Array.Empty<string>();
        public string? DiscrepancyDescription { get; init; }
    }

    /// <summary>
    /// Default verifier implementation using reflection-based hint checking.
    /// </summary>
    public class PostExecutionVerifier : IPostExecutionVerifier
    {
        // TODO(impl): for each VerificationHint key, look up the entity and
        // compare actual state to expected value. Examples:
        //   "colonist_123_drafted:true" → check pawn.drafter.Drafted
        //   "position:45,32" → check pawn.Position == new IntVec3(45,0,32)
        //   "bill_789_suspended:true" → check bill.suspended
    }
}
```

### 8.1 Verifier Strategy per Command Family

| Family | Verification Approach |
|---|---|
| Work priorities | Read `pawn.workSettings.GetPriority(WorkTypeDef)` after assignment |
| Bills | Check `bill.suspended`, `bill.repeatMode`, `bill.targetCount` |
| Zones | Confirm `zone.Cells` count matches expected; zone exists in `map.zoneManager` |
| Designations | Check `map.designationManager.DesignationOn(thing, def)` |
| Drafting | Read `pawn.Drafted`, `pawn.stances.curStance`, `pawn.mindState` orders |
| Rescue/Capture | Check `pawn.CurJob.def == JobDefOf.Rescue/Capture` and destination bed assignment |
| Equipment | Verify `pawn.equipment.Primary.def.defName` matches |
| Research | Confirm `Find.ResearchManager.currentProj.defName` matches |
| Trade | Verify trade dialog opened or caravan formed |

## 9. Failure Reporting (& Feedback Loop)

```csharp
namespace RimAI.Agent.Action
{
    /// <summary>
    /// Aggregated per-tick execution report. Fed back to Cognition's WorkingMemory
    /// so the planner knows what succeeded, what failed, and why.
    /// </summary>
    public class ActionFeedback
    {
        public int TickExecuted { get; init; }                        // RimWorld tick number
        public int TotalCommandsReceived { get; init; }
        public int CommandsExecuted { get; init; }
        public int CommandsSucceeded { get; init; }
        public int CommandsFailed { get; init; }
        public int CommandsDropped { get; init; }                     // exceeded queue depth
        public CommandFailureReport[] Failures { get; init; } = Array.Empty<CommandFailureReport>();
    }

    public class CommandFailureReport
    {
        public string CommandId { get; init; } = "";
        public ActionCommandType Type { get; init; }
        public string ErrorMessage { get; init; } = "";
        public FailureCategory Category { get; init; }
        public Dictionary<string, object?> OriginalParameters { get; init; } = new();
    }

    public enum FailureCategory
    {
        EntityNotFound,      // colonist_id/item_id/zone_id doesn't exist in current map
        PathUnreachable,      // target cell/item is unreachable
        InsufficientSkill,    // colonist lacks required skill level
        ResourceMissing,      // required materials/medicine not available
        GameStateConflict,   // command conflicts with current game state (e.g. already drafted)
        ValidationError,     // handler Validate() rejected parameters
        UnknownError,        // unexpected exception during execution
        Dropped              // queue overflow, never attempted
    }
}
```

## 10. Interaction with Other Layers

| Layer | Direction | What flows |
|---|---|---|
| Cognition → Action | inbound | `ActionCommand[]` (validated DTOs, thread-safe enqueue) |
| Action → Cognition | outbound (via Memory) | `ActionFeedback` — success/failure per command |
| Action → RimWorld | internal | All `Verse`/`RimWorld` API calls happen inside `ICommandHandler` implementations |
| Orchestration → Action | control | Tick invocation (`ExecutePendingCommands`), queue depth config |

**Thread-safety contract:**
- `EnqueueCommands` — callable from any thread (uses `ConcurrentQueue`)
- `ExecutePendingCommands` — main thread ONLY (accesses `Find.CurrentMap`, `Verse.Pawn`, etc.)
- `ICommandHandler.Execute` / `Validate` — main thread ONLY
- `IPostExecutionVerifier.Verify` — main thread ONLY

## 11. Acceptance Criteria by Milestone

### M2 — Action (first testable)
- [ ] `ActionExecutor` accepts `ActionCommand[]`, executes on main thread, returns `ActionFeedback`.
- [ ] The **core-12** `ActionCommandType` values (06 G-13) have registered `ICommandHandler`s; all other enum values dispatch to `ValidationError("not yet implemented")` without throwing.
- [ ] `MainThreadCommandQueue` thread-safety: enqueue from 4 background threads simultaneously,
  drain on main thread, no lost or duplicated commands.
- [ ] `PostExecutionVerifier` confirms at least 80% of commands by verification hints.
- [ ] Scripted (no-LLM) command file playback: JSON file of 20 commands drives the colony
  (draft, move, undraft, set work priorities, add bills).
- [ ] Failed commands produce `CommandFailureReport` with correct `FailureCategory`.

### M3 — Reactive Loop Integration
- [ ] Reactive commands (`DraftColonist`, `UndraftColonist`, `MoveTo`, `AttackTarget`,
  `RescueColonist`, `TendPawn`) execute within 1 game-tick of enqueue.
- [ ] `ActionFeedback` from reactive executions flows to WorkingMemory and is visible
  in the next Cognition prompt.

### M4 — Tactical Loop Integration
- [ ] Work priority changes, bill management, zone operations execute and verify.
- [ ] Queue handles 50+ commands per tick without frame drops.

### M5 — Strategic Loop Integration
- [ ] Research, trade, caravan, and policy commands execute and verify.
- [ ] `CancelCommand` works for any previously issued command.

## 12. Edge Cases

| Case | Behavior |
|---|---|
| Command references entity that died/despawned between Cognition and execution | `Validate()` returns "EntityNotFound"; command rejected before Execute; logged |
| Two commands conflict (e.g., draft+undraft same pawn in same tick) | Execute in queue order; second command wins; log conflict |
| Command target cell is outside map bounds | `Validate()` rejects with "PathUnreachable" |
| Queue reaches `MaxQueueDepth` (default: 100) | Drop lowest-priority commands first; increment `CommandsDropped`; log warning |
| Handler throws unhandled exception during Execute | Catch in dispatcher; return `CommandExecutionResult` with `UnknownError`; log full stack trace; never crash the Unity main thread |
| Same command re-enqueued (duplicate CommandId) | Skip duplicate; log info |
| Map is null (game loading / no colony loaded) | `ExecutePendingCommands` returns early with empty result; queue preserved |
| `VerificationHints` references an entity that no longer exists | Verifier skips that hint; marks `Verified=true` with note; does not fail |
| LLM sends 500 commands in one batch (attempted griefing) | `EnqueueBatch` caps at `MaxQueueDepth`; excess dropped; Orchestrator's per-plan cap (20) should prevent this |

## 13. Unit-Test Strategy

1. **ActionCommand serialization** — round-trip all 30 command types through JSON;
   verify `Parameters` survive deserialization with correct types.
2. **MainThreadCommandQueue** — enqueue 100 commands from 4 parallel tasks; assert
   drain yields all 100, sorted by priority then timestamp, no duplicates.
3. **Enqueue overflow** — with `MaxQueueDepth=10`, enqueue 50; assert exactly 10 kept
   (lowest priority), 40 dropped; verify `CommandsDropped=40`.
4. **CommandDispatcher registration** — register all handlers; assert dispatch for
   every `ActionCommandType` returns non-null `CommandExecutionResult`.
5. **ICommandHandler.Validate** — for each handler, test valid params (pass),
   missing required key (fail), invalid ID format (fail), entity-not-found (fail).
6. **PostExecutionVerifier** — mock verification hints; test all-hints-confirmed (pass),
   one-hint-mismatch (fail with discrepancy), entity-gone (skip gracefully).
7. **Fake command file playback** — JSON fixture with 20 commands; execute through
   `ActionExecutor` with mock handlers; verify `ActionFeedback.TotalCommandsReceived=20`
   and correct success/failure counts.

**Test data:** JSON fixtures for each command type under `RimAI.Agent.Tests/Fixtures/ActionCommands/`.

---
**STATUS: DOCS-B PASS** — ~260 lines
