using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using RimAI.Agent.Action;
using RimAI.Framework.Contracts;

namespace RimAI.Agent.Cognition
{
    // --- Prompt Assembly ---

    /// <summary>
    /// Pure function: assembles a list of ChatMessage from snapshot + memory DTOs.
    /// No LLM calls, no IO, no game references. Fully unit-testable.
    /// </summary>
    public interface IPromptAssembler
    {
        /// <summary>Build system prompt for a planning tier.</summary>
        // TODO(impl): BuildSystemPrompt — loads tier-specific template from embedded Resources/Prompts/{Tier}System.txt.
        //   Template includes: role definition, tool catalog (one-line per tool), output constraints,
        //   budget awareness (maxRounds), snapshot schema summary, safety rules.
        //   Returns ChatMessage with Role="system" and populated Content.
        ChatMessage BuildSystemPrompt(PlanningTier tier, Perception.ColonySnapshot snapshot);

        /// <summary>Build user-prompt context (snapshot summary + recent events).</summary>
        // TODO(impl): BuildContextPrompt — assembles user-facing context from snapshot summary, recent EpisodicLog events,
        //   and WorkingMemory window. Tier-specific content: Reactive gets 10 events, Tactical 30 + semantic,
        //   Strategic 50 + semantic + goals.
        //   Returns ChatMessage with Role="user" and populated Content.
        ChatMessage BuildContextPrompt(
            PlanningTier tier,
            Perception.ColonySnapshot snapshot,
            EpisodicLog events,
            Memory.WorkingMemory memory);

        /// <summary>Build a history continuation prompt for tool-loop follow-ups.</summary>
        // TODO(impl): BuildContinuationPrompt — for multi-round tool-calling loops.
        //   Constructs a user message incorporating conversation history + last tool result.
        //   Used when LLM needs to correct an invalid tool call or continue after tool execution.
        //   Returns ChatMessage with Role="user" and populated Content.
        ChatMessage BuildContinuationPrompt(
            List<ChatMessage> conversationSoFar,
            string lastToolResult);
    }

    // TODO(impl): PlanningTier — tier enumeration for prompt assembly and tool filtering.
    //   Reactive: event-driven, combat/medical tools only, <1 game-hour scope.
    //   Tactical: scheduled ~6h, work/bill/zone/designation tools.
    //   Strategic: scheduled ~2d, research/trade/caravan/policy tools.
    public enum PlanningTier
    {
        Reactive,  // TODO(impl): combat + medical tools only; 10 events; 300 token budget
        Tactical,  // TODO(impl): work + bill + zone + designation tools; 30 events + top 3 semantic; 800 token budget
        Strategic  // TODO(impl): all tools; 50 events + top 10 semantic; 1,500 token budget
    }

    // --- Tool Definitions ---

    /// <summary>
    /// Provides ToolDefinition[] matching the RimAIApi contract.
    /// Every tool maps 1:1 to an ActionCommandType in the whitelist.
    /// Adding a new action requires: (1) new ToolDefinition here,
    /// (2) new handler in Action layer, (3) updated whitelist schema.
    /// </summary>
    public interface IToolDefinitionProvider
    {
        /// <summary>All tools available to the planner.</summary>
        // TODO(impl): GetAllTools — returns all 24+ tool definitions loaded from Resources/Tools/*.json.
        //   Each ToolDefinition has Type="function" and Function JObject with name/description/parameters schema.
        //   Stateless, cached on first call.
        List<ToolDefinition> GetAllTools();

        /// <summary>Subset filtered by tier (Reactive gets combat/medical only, etc.).</summary>
        // TODO(impl): GetToolsForTier — filters tool catalog by PlanningTier:
        //   Reactive: draft/undraft/move-to/attack-target/rescue/capture/tend/equip
        //   Tactical: work-priorities/bills/zones/designations/schedule/policies + reactive tools
        //   Strategic: research/trade/caravan/policy + all others
        List<ToolDefinition> GetToolsForTier(PlanningTier tier);
    }

    /// <summary>
    /// Concrete provider — stateless, loads JSON schemas from embedded resources.
    /// </summary>
    public class ToolDefinitionProvider : IToolDefinitionProvider
    {
        // TODO(impl): load from Resources/Tools/*.json, cache in static readonly dictionary
        // TODO(impl): tier mapping — static HashSet<string> per tier listing tool names

        public List<ToolDefinition> GetAllTools()
        {
            throw new NotImplementedException();
        }

        public List<ToolDefinition> GetToolsForTier(PlanningTier tier)
        {
            throw new NotImplementedException();
        }
    }

    // --- Tool-Calling Loop ---

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
        // TODO(impl): ExecuteAsync — runs the tool-calling loop:
        //   1. Clone session.Messages
        //   2. Loop up to session.MaxRounds:
        //      a. Call RimAIApi.GetCompletionWithToolsAsync(messages, tools, conversationId, ct)
        //      b. If response.IsFailure: return PlanResult with error
        //      c. If FinishReason == "stop": break (LLM done)
        //      d. For each toolCall in response.Message.ToolCalls:
        //         - Validate via IActionCommandValidator.Validate
        //         - If valid: add command to results, append "ok" tool result message
        //         - If invalid: append error tool result message (LLM can retry)
        //      e. Append assistant message (with tool calls) to messages
        //   3. Return PlanResult with gathered commands, rounds used, tokens used
        //   CancellationToken cancels gracefully; returns partial PlanResult.
        Task<PlanResult> ExecuteAsync(
            PlanningSession session,
            CancellationToken ct);
    }

    public class PlanningSession
    {
        // TODO(impl): ConversationId — unique session ID for cache scoping in RimAIApi
        public string ConversationId { get; init; }

        // TODO(impl): Tier — which planning tier this session belongs to
        public PlanningTier Tier { get; init; }

        // TODO(impl): Messages — initial conversation (system prompt + context)
        public List<ChatMessage> Messages { get; init; }

        // TODO(impl): Tools — tool definitions available for this session (tier-filtered)
        public List<ToolDefinition> Tools { get; init; }

        // TODO(impl): MaxRounds — max tool-calling rounds before forced exit (from config)
        public int MaxRounds { get; init; }

        // TODO(impl): ModelName — which model to use (from CognitionEngineConfig per tier)
        public string ModelName { get; init; }
    }

    public class PlanResult
    {
        // TODO(impl): IsSuccess — true if the planning session completed without fatal errors
        public bool IsSuccess { get; init; }

        // TODO(impl): Commands — validated ActionCommand[] from the session, empty array if none
        public ActionCommand[] Commands { get; init; } = Array.Empty<ActionCommand>();

        // TODO(impl): ErrorMessage — populated on failure (LLM API error, timeout, etc.), null on success
        public string ErrorMessage { get; init; }

        // TODO(impl): RoundsUsed — number of tool-calling rounds consumed (1+)
        public int RoundsUsed { get; init; }

        // TODO(impl): TotalTokensUsed — total tokens consumed by LLM calls in this session
        public int TotalTokensUsed { get; init; }

        // TODO(impl): FinishReason — "stop", "length", "tool_calls", "max_rounds", "cancelled"
        public string FinishReason { get; init; } = "unknown";
    }

    // --- ActionCommand Validation ---

    /// <summary>
    /// Validates LLM-generated tool calls against the JSON-schema whitelist.
    /// Rejects unknown tools, invalid parameters, and out-of-range values.
    /// This is the security boundary between LLM output and game mutation.
    /// </summary>
    public interface IActionCommandValidator
    {
        /// <summary>Validate a single tool call into an ActionCommand or rejection.</summary>
        // TODO(impl): Validate — check tool name against whitelist, parse JSON arguments,
        //   verify all required params present, check enum values, numeric ranges, string ID format.
        //   Unknown properties stripped (not an error). Returns ValidationResult with command or error.
        ValidationResult Validate(ToolCall toolCall);

        /// <summary>Batch-validate; rejects individual failures, returns only valid.</summary>
        // TODO(impl): ValidateBatch — validates each ToolCall independently.
        //   Returns ValidationBatchResult with valid commands array and failures array.
        //   Individual failures don't block other validations.
        ValidationBatchResult ValidateBatch(IReadOnlyList<ToolCall> toolCalls);
    }

    public class ValidationResult
    {
        // TODO(impl): IsValid — true if tool call passed all validation rules
        public bool IsValid { get; init; }

        // TODO(impl): Command — populated ActionCommand if valid, null if not
        public ActionCommand Command { get; init; }

        // TODO(impl): Error — validation error message if invalid, null if valid
        public string Error { get; init; }
    }

    public class ValidationBatchResult
    {
        // TODO(impl): ValidCommands — array of successfully validated ActionCommands
        public ActionCommand[] ValidCommands { get; init; }

        // TODO(impl): Failures — array of ValidationFailure for each rejected tool call
        public ValidationFailure[] Failures { get; init; }
    }

    public class ValidationFailure
    {
        // TODO(impl): ToolCallIndex — index of the failing ToolCall in the input list
        public int ToolCallIndex { get; init; }

        // TODO(impl): ToolName — name of the failing tool
        public string ToolName { get; init; } = "";

        // TODO(impl): Error — human-readable validation error description
        public string Error { get; init; } = "";

        // TODO(impl): RawArguments — the JSON arguments string that failed validation
        public string RawArguments { get; init; } = "";
    }
}
