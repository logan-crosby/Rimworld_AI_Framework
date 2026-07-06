# 02 — Cognition Layer Spec

**Status:** draft | **Implements:** RimAI.Agent.Cognition
**Amendments (06-plan-audit.md wins):** `PlanningTier` is renamed to the canonical **`CognitionTier`** in `RimAI.Agent.Shared` (G-10). `GameAlert` does not exist — reactive signatures take **`PerceptionEvent trigger`** (G-11). Cognition calls the LLM via **`ILlmClient`** (G-07), never static `RimAIApi`. Tool catalog §6.1 is extended with the 10 unmapped command types + construction/growing/prisoner tools — post-audit total 41 (G-12, G-04).
**Prerequisite specs:** 00-MASTER-PLAN.md, 01-perception.md, 04-memory.md
**Consumed by:** 03-action.md, 05-orchestration.md

## 1. Purpose

The Cognition layer is a three-tier LLM-driven planner that translates colony snapshots (from Perception) and episodic memory (from Memory) into validated `ActionCommand` DTOs (consumed by Action). It is the only layer that calls the LLM — all other layers are deterministic game code. It produces **no game state mutation**, **no Verse/RimWorld type references**, and **no side effects beyond DTO output**.

## 2. Architecture

```
Perception ──► ColonySnapshot (JSON DTO)
Memory ──────► EpisodicLog + WorkingMemory (JSON DTOs)
                    │
                    ▼
         ┌─────────────────────────────┐
         │       PromptAssembler       │  ← pure function: DTOs → string
         └─────────────┬───────────────┘
                       ▼
         ┌─────────────────────────────┐
         │  TierDispatcher (selects    │
         │  Strategic/Tactical/Reactive│
         └─────────────┬───────────────┘
                       ▼
         ┌─────────────────────────────┐
         │   ToolCallingLoop           │  ← async, off-thread
         │   via RimAIApi.GetCompletion│
         │   WithToolsAsync            │
         └─────────────┬───────────────┘
                       ▼
         ┌─────────────────────────────┐
         │   ActionCommandValidator    │  ← JSON-schema whitelist
         └─────────────┬───────────────┘
                       ▼
         Action ──► ActionCommand[] (validated DTOs)
```

## 3. Namespace & Entry Point

```csharp
namespace RimAI.Agent.Cognition
{
    /// <summary>
    /// Primary entry point. Invoked by Orchestrator on its tick cadence.
    /// All methods return validated ActionCommand[] — never game types.
    /// </summary>
    public interface ICognitionEngine
    {
        /// <summary>
        /// Reactive tier: event-driven, <1 game-hour scope.
        /// Called by Orchestrator when EventBus fires a priority alert
        /// (raid, fire, mental break, medical emergency, predator, siege).
        /// </summary>
        Task<ActionCommand[]> PlanReactiveAsync(
            GameAlert alert,
            ColonySnapshot snapshot,
            WorkingMemory memory,
            CancellationToken ct);

        /// <summary>
        /// Tactical tier: scheduled ~every 6 game-hours.
        /// Work priorities, bills, zones, hauling, medical ops, animal handling.
        /// </summary>
        Task<ActionCommand[]> PlanTacticalAsync(
            ColonySnapshot snapshot,
            EpisodicLog recentEvents,
            WorkingMemory memory,
            CancellationToken ct);

        /// <summary>
        /// Strategic tier: scheduled ~every 2 game-days.
        /// Research path, expansion, wealth/threat balance, recruitment, trade.
        /// </summary>
        Task<ActionCommand[]> PlanStrategicAsync(
            ColonySnapshot snapshot,
            EpisodicLog history,
            WorkingMemory memory,
            CancellationToken ct);
    }

    /// <summary>
    /// Concrete implementation. Stateless; all state lives in Memory layer.
    /// Constructed with tier-specific model names and prompt templates.
    /// </summary>
    public class CognitionEngine : ICognitionEngine
    {
        // constructor: ICognitionEngineConfig config, IPromptAssembler assembler,
        //              IToolDefinitionProvider tools, IActionCommandValidator validator

        // TODO(impl): each Plan*Async method
    }
}
```

## 4. Tier Configuration (DTO)

```csharp
namespace RimAI.Agent.Cognition
{
    /// <summary>
    /// Immutable config injected by Orchestrator at construction.
    /// </summary>
    public class CognitionEngineConfig
    {
        public string ReactiveModelName { get; init; }   // default: "deepseek-v4-flash"
        public string TacticalModelName { get; init; }    // default: "deepseek-v4-pro"
        public string StrategicModelName { get; init; }   // default: "deepseek-v4-pro"
        public int MaxToolCallRounds { get; init; } = 5;  // prevent infinite loops
        public int MaxCommandsPerPlan { get; init; } = 20;
        public float ReactiveBudgetWeight { get; init; } = 0.1f;   // 10% of daily budget
        public float TacticalBudgetWeight { get; init; } = 0.4f;   // 40%
        public float StrategicBudgetWeight { get; init; } = 0.5f;  // 50%
    }
}
```

## 5. Prompt Assembly

```csharp
namespace RimAI.Agent.Cognition
{
    /// <summary>
    /// Pure function: assembles a list of ChatMessage from snapshot + memory DTOs.
    /// No LLM calls, no IO, no game references. Fully unit-testable.
    /// </summary>
    public interface IPromptAssembler
    {
        /// <summary>Build system prompt for a planning tier.</summary>
        ChatMessage BuildSystemPrompt(PlanningTier tier, ColonySnapshot snapshot);

        /// <summary>Build user-prompt context (snapshot summary + recent events).</summary>
        ChatMessage BuildContextPrompt(
            PlanningTier tier,
            ColonySnapshot snapshot,
            EpisodicLog events,
            WorkingMemory memory);

        /// <summary>Build a history continuation prompt for tool-loop follow-ups.</summary>
        ChatMessage BuildContinuationPrompt(
            List<ChatMessage> conversationSoFar,
            string lastToolResult);
    }

    public enum PlanningTier { Reactive, Tactical, Strategic }
}
```

### 5.1 System Prompt Template (conceptual, stored as embedded resource)

The assembler loads tier-specific templates from `Resources/Prompts/ReactiveSystem.txt`,
`TacticalSystem.txt`, `StrategicSystem.txt`. Each template includes:

- **Role definition** — "You are a RimWorld colony planner at the {tier} tier…"
- **Tool catalog** — enumerated tool names with one-line descriptions
- **Output constraints** — "You MUST call tools to express every action. Never output free text."
- **Budget awareness** — "You have {maxRounds} tool-calling rounds. Be efficient."
- **Snapshot schema summary** — key fields from `ColonySnapshot` relevant to this tier
- **Safety rules** — "Never suggest commands that would kill the colony. Prioritize survival."

## 6. Tool Definitions (LLM Function Schemas)

```csharp
namespace RimAI.Agent.Cognition
{
    /// <summary>
    /// Provides ToolDefinition[] matching the RimAIApi contract.
    /// Every tool maps 1:1 to an ActionCommandType in the whitelist.
    /// Adding a new action requires: (1) new ToolDefinition here,
    /// (2) new handler in Action layer, (3) updated whitelist schema.
    /// </summary>
    public interface IToolDefinitionProvider
    {
        /// <summary>All tools available to the planner.</summary>
        List<ToolDefinition> GetAllTools();

        /// <summary>Subset filtered by tier (Reactive gets combat/medical only, etc.).</summary>
        List<ToolDefinition> GetToolsForTier(PlanningTier tier);
    }

    /// <summary>
    /// Concrete provider — stateless, loads JSON schemas from embedded resources.
    /// </summary>
    public class ToolDefinitionProvider : IToolDefinitionProvider
    {
        // TODO(impl): load from Resources/Tools/*.json, cache
    }
}
```

### 6.1 Representative Tool JSON Schema

Each tool is a `ToolDefinition` with `Type = "function"` and a `Function` JObject containing the
OpenAI-compatible function schema. Example:

```json
{
  "name": "set_work_priority",
  "description": "Assign a work priority for a colonist on a work type.",
  "parameters": {
    "type": "object",
    "properties": {
      "colonist_id": { "type": "string", "description": "Colonist ID from snapshot" },
      "work_type": {
        "type": "string",
        "enum": ["Firefighter","Doctor","Patient","Warden","Handling","Cooking",
                 "Construction","Growing","Mining","Artistic","Crafting","Hauling",
                 "Cleaning","Research"]
      },
      "priority": { "type": "integer", "minimum": 0, "maximum": 4 }
    },
    "required": ["colonist_id", "work_type", "priority"]
  }
}
```

Full tool catalog (mirrors Action whitelist — see §8 of 03-action.md):

| Tool name | Tier(s) | ActionCommandType |
|---|---|---|
| `set_work_priority` | Tactical | SetWorkPriority |
| `add_bill` | Tactical | AddBill |
| `remove_bill` | Tactical | RemoveBill |
| `create_zone` | Tactical, Strategic | CreateZone |
| `delete_zone` | Tactical, Strategic | DeleteZone |
| `assign_to_zone` | Tactical | AssignToZone |
| `add_designation` | Tactical | AddDesignation |
| `remove_designation` | Tactical | RemoveDesignation |
| `draft_colonist` | Reactive | DraftColonist |
| `undraft_colonist` | Reactive | UndraftColonist |
| `move_to` | Reactive, Tactical | MoveTo |
| `attack_target` | Reactive | AttackTarget |
| `set_research_project` | Strategic | SetResearchProject |
| `initiate_trade` | Strategic | InitiateTrade |
| `set_outfit_policy` | Tactical, Strategic | SetOutfitPolicy |
| `set_drug_policy` | Tactical, Strategic | SetDrugPolicy |
| `set_food_policy` | Tactical | SetFoodPolicy |
| `rescue_colonist` | Reactive | RescueColonist |
| `capture_prisoner` | Reactive | CapturePrisoner |
| `tend_pawn` | Reactive | TendPawn |
| `equip_item` | Reactive, Tactical | EquipItem |
| `set_schedule` | Tactical | SetSchedule |
| `form_caravan` | Strategic | FormCaravan |
| `cancel_command` | All | CancelCommand |

## 7. Tool-Calling Loop

```csharp
namespace RimAI.Agent.Cognition
{
    /// <summary>
    /// Drives the LLM tool-calling conversation loop.
    /// Calls RimAIApi.GetCompletionWithToolsAsync, parses tool calls,
    /// validates each, and feeds results back until stop-reason or max rounds.
    /// </summary>
    public interface IToolCallingLoop
    {
        /// <summary>
        /// Execute the tool-calling loop for a planning session.
        /// Returns only validated ActionCommand DTOs.
        /// </summary>
        Task<PlanResult> ExecuteAsync(
            PlanningSession session,
            CancellationToken ct);
    }

    public class PlanningSession
    {
        public string ConversationId { get; init; }
        public PlanningTier Tier { get; init; }
        public List<ChatMessage> Messages { get; init; }       // system + context
        public List<ToolDefinition> Tools { get; init; }
        public int MaxRounds { get; init; }
        public string ModelName { get; init; }
    }

    public class PlanResult
    {
        public bool IsSuccess { get; init; }
        public ActionCommand[] Commands { get; init; } = Array.Empty<ActionCommand>();
        public string? ErrorMessage { get; init; }
        public int RoundsUsed { get; init; }
        public int TotalTokensUsed { get; init; }
        public string FinishReason { get; init; } = "unknown";
    }
}
```

### 7.1 Loop Algorithm (pseudocode)

```
function ExecuteAsync(session):
    rounds = 0
    messages = clone(session.Messages)
    commands = []

    while rounds < session.MaxRounds:
        response = await RimAIApi.GetCompletionWithToolsAsync(
            messages, session.Tools, session.ConversationId, ct)

        if response.IsFailure:
            return PlanResult.Failure("LLM call failed: " + response.Error)

        if response.Value.FinishReason == "stop":
            break  // LLM done, no more tool calls

        rounds++

        messages.Add(response.Value.Message)  // assistant message FIRST (OpenAI wire order — 06 G-14)

        for each toolCall in response.Value.Message.ToolCalls:
            validated = ActionCommandValidator.Validate(toolCall)
            if validated.IsValid:
                commands.Add(validated.Command)
                // Honest ack: command is queued, not executed (06 G-14).
                // Execution results reach the NEXT session via ActionFeedback → WorkingMemory.
                messages.Add(toolResultMessage("accepted (queued for execution; result in next briefing)", toolCall.Id))
            else:
                // Feed error back so LLM can correct
                messages.Add(toolResultMessage("error: " + validated.Error, toolCall.Id))

    return PlanResult.Success(commands, rounds, tokensUsed)
```

## 8. ActionCommand Validation

```csharp
namespace RimAI.Agent.Cognition
{
    /// <summary>
    /// Validates LLM-generated tool calls against the JSON-schema whitelist.
    /// Rejects unknown tools, invalid parameters, and out-of-range values.
    /// This is the security boundary between LLM output and game mutation.
    /// </summary>
    public interface IActionCommandValidator
    {
        /// <summary>Validate a single tool call into an ActionCommand or rejection.</summary>
        ValidationResult Validate(ToolCall toolCall);

        /// <summary>Batch-validate; rejects individual failures, returns only valid.</summary>
        ValidationBatchResult ValidateBatch(IReadOnlyList<ToolCall> toolCalls);
    }

    public class ValidationResult
    {
        public bool IsValid { get; init; }
        public ActionCommand? Command { get; init; }
        public string? Error { get; init; }
    }

    public class ValidationBatchResult
    {
        public ActionCommand[] ValidCommands { get; init; }
        public ValidationFailure[] Failures { get; init; }
    }

    public class ValidationFailure
    {
        public int ToolCallIndex { get; init; }
        public string ToolName { get; init; } = "";
        public string Error { get; init; } = "";
        public string RawArguments { get; init; } = "";
    }
}
```

### 8.1 Validation Rules (per tool)

1. **Tool name** must match an entry in the whitelist exactly (case-sensitive).
2. **All `required` parameters** must be present.
3. **Enum parameters** must match one of the listed values.
4. **Numeric parameters** must fall within `minimum`/`maximum` bounds.
5. **String IDs** (`colonist_id`, `zone_id`, `bill_id`, etc.) must conform to the ID format
   (`^[a-zA-Z0-9_-]{1,64}$`) but are NOT validated against snapshot contents in this layer —
   that is the Action layer's job (existence check on the main thread).
6. **Array parameters** must not exceed declared `maxItems`.
7. **Unknown properties** in the arguments object are stripped (not an error; LLMs hallucinate extras).

## 9. Interaction with Other Layers

| Layer | Direction | What flows |
|---|---|---|
| Perception → Cognition | inbound | `ColonySnapshot` DTO (JSON), `GameAlert` events |
| Memory → Cognition | inbound | `EpisodicLog` + `WorkingMemory` DTOs for prompt assembly |
| Cognition → Framework | call out | `RimAIApi.GetCompletionWithToolsAsync()` with tool definitions |
| Cognition → Action | outbound | `ActionCommand[]` (validated, serializable DTOs) |
| Orchestration → Cognition | control | Tick invocation, tier selection, budget checks, cancellation |

**Contract:** Cognition never touches `Verse`, `RimWorld`, `Pawn`, `Map`, `Thing`, or any game type.
It never accesses the Unity main thread directly. Its only async boundary is the `RimAIApi` call.

## 10. Acceptance Criteria by Milestone

### M3 — Reactive Loop (first closed loop)
- [ ] `PlanReactiveAsync` receives a `GameAlert` + `ColonySnapshot` and returns `ActionCommand[]`.
- [ ] Reactive tool set includes: `draft_colonist`, `undraft_colonist`, `move_to`,
  `attack_target`, `rescue_colonist`, `capture_prisoner`, `tend_pawn`, `equip_item`.
- [ ] Tool-calling loop exits within 3 rounds without hanging.
- [ ] LLM responds to a raid alert with at minimum draft + move commands.
- [ ] Unit tests: mock `RimAIApi`, feed known tool-call responses, assert correct `ActionCommand[]`
  output for 5 alert scenarios (raid, fire, break, medical, predator).

### M4 — Tactical Planner
- [ ] `PlanTacticalAsync` produces work-priority, bill, zone, and designation commands.
- [ ] `PromptAssembler.BuildSystemPrompt` includes full snapshot context (pawns, skills, work tables).
- [ ] Tool-calling loop handles 5+ rounds with error correction (invalid param → LLM retries).
- [ ] Unit tests: 10 scenario inputs with expected command types.

### M5 — Strategic Planner
- [ ] `PlanStrategicAsync` produces research, trade, recruitment, and expansion commands.
- [ ] Prompt includes long-term history from `EpisodicLog` + embedding context.
- [ ] Degradation order verified: when budget exhausted, Strategic skipped before Tactical.
- [ ] Unit tests: 5 multi-turn strategic scenarios.

## 11. Edge Cases

| Case | Behavior |
|---|---|
| LLM returns `finish_reason: "length"` mid-loop | Treat as truncation; commit commands gathered so far; log warning |
| LLM returns zero tool calls with `finish_reason: "stop"` | Valid — planner decided no action needed; return empty array |
| LLM hallucinates a non-whitelisted tool name | Validator rejects; error fed back to LLM; if repeated >2x, abort loop |
| Tool call arguments fail JSON parse | Validator rejects with parse error; LLM gets the error message |
| `GetCompletionWithToolsAsync` throws / returns error `Result` | Abort loop; return `PlanResult` with error; Orchestrator handles retry |
| `MaxRounds` reached with unconsumed tool calls | Commit all valid commands gathered; log "max rounds exhausted" |
| `CancellationToken` fires mid-loop | Cancel gracefully; return partial `PlanResult` with valid commands so far |
| Snapshot is empty (colony wiped) | Prompt assembler produces minimal context; LLM likely returns empty plan |
| Conversation history exceeds model context window | PromptAssembler trims oldest messages, preserving system prompt + last 2 rounds |

## 12. Unit-Test Strategy

Cognition is the **most testable** layer — zero game dependencies:

1. **PromptAssembler** — pure function tests: given known `ColonySnapshot` + `EpisodicLog` fixtures,
   assert output `ChatMessage` list has correct role ordering, contains key fields from fixture.
2. **ActionCommandValidator** — schema conformance tests: for each tool, test valid args (pass),
   missing required args (reject), out-of-range numerics (reject), invalid enums (reject),
   hallucinated tool name (reject), extra unknown properties (stripped, still pass).
3. **ToolCallingLoop** — mock `RimAIApi` (via `IRimAIApi` wrapper interface):
   inject sequences of mock `UnifiedChatResponse` values simulating multi-round tool use;
   assert final `ActionCommand[]` matches expected, `RoundsUsed` is correct, error propagation works.
4. **TierDispatcher** — verify correct tool subset per tier, correct prompt template selection.
5. **End-to-end** — `CognitionEngine.PlanReactiveAsync(mockApi, fixtureAlert, fixtureSnapshot, fixtureMemory)`
   → `ActionCommand[]` with expected command types.

**Test data:** Create JSON fixtures for 3 colony states (early game 3-pawn, mid game 8-pawn, late game 20-pawn)
and 5 alert types. Store under `RimAI.Agent.Tests/Fixtures/`.

---
**STATUS: DOCS-B PASS** — ~275 lines
