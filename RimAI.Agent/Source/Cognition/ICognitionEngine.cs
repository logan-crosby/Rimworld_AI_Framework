using System;
using System.Threading;
using System.Threading.Tasks;
using RimAI.Framework.Contracts;

namespace RimAI.Agent.Cognition
{
    // TODO(impl): GameAlert — a priority alert surfaced by the Perception layer via EventBus.
    //   Represents an urgent event (raid, fire, mental break, medical emergency, predator, siege)
    //   that triggers reactive planning in the Cognition layer.
    //   Carried through EventBus as PerceptionEvent with CriticalLevel >= Medium.
    public class GameAlert
    {
        // TODO(impl): AlertType — matches PerceptionEventType for the triggering event
        public string AlertType { get; init; }

        // TODO(impl): Tick — game tick when the alert was generated
        public int Tick { get; init; }

        // TODO(impl): SourcePawnId — pawn.ThingID of involved pawn, null for map-level alerts
        public string SourcePawnId { get; init; }

        // TODO(impl): Message — human-readable alert description
        public string Message { get; init; }

        // TODO(impl): Metadata — type-specific key-value pairs (faction, severity, location, etc.)
        public System.Collections.Generic.Dictionary<string, string> Metadata { get; init; }
    }

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
        // TODO(impl): PlanReactiveAsync — receives GameAlert + ColonySnapshot + WorkingMemory.
        //   Returns ActionCommand[] targeting draft/undraft, move-to, attack-target, rescue, capture, tend, equip.
        //   Reactive tool set limited to combat/medical commands only.
        //   Runs off-thread (Task.Run by Orchestrator), no Verse types in parameters.
        //   CancellationToken allows Orchestrator to cancel mid-flight on pause/mod-unload.
        Task<Action.ActionCommand[]> PlanReactiveAsync(
            GameAlert alert,
            Perception.ColonySnapshot snapshot,
            Memory.WorkingMemory memory,
            CancellationToken ct);

        /// <summary>
        /// Tactical tier: scheduled ~every 6 game-hours.
        /// Work priorities, bills, zones, hauling, medical ops, animal handling.
        /// </summary>
        // TODO(impl): PlanTacticalAsync — receives ColonySnapshot + EpisodicLog + WorkingMemory.
        //   Returns ActionCommand[] for work/bill/zone/designation/schedule ops.
        //   Uses TacticalModelName from config. Runs off-thread.
        //   recentEvents from EventLog.GetRecent() or equivalent, filtered to tactical scope.
        Task<Action.ActionCommand[]> PlanTacticalAsync(
            Perception.ColonySnapshot snapshot,
            EpisodicLog recentEvents,
            Memory.WorkingMemory memory,
            CancellationToken ct);

        /// <summary>
        /// Strategic tier: scheduled ~every 2 game-days.
        /// Research path, expansion, wealth/threat balance, recruitment, trade.
        /// </summary>
        // TODO(impl): PlanStrategicAsync — receives ColonySnapshot + EpisodicLog (longer history) + WorkingMemory.
        //   Returns ActionCommand[] for research/trade/caravan/policy/expansion commands.
        //   Uses StrategicModelName from config. Runs off-thread.
        //   history spans a wider date range than tactical recentEvents.
        Task<Action.ActionCommand[]> PlanStrategicAsync(
            Perception.ColonySnapshot snapshot,
            EpisodicLog history,
            Memory.WorkingMemory memory,
            CancellationToken ct);
    }

    /// <summary>
    /// Concrete implementation. Stateless; all state lives in Memory layer.
    /// Constructed with tier-specific model names and prompt templates.
    /// </summary>
    public class CognitionEngine : ICognitionEngine
    {
        // TODO(impl): constructor — ICognitionEngineConfig config, IPromptAssembler assembler,
        //   IToolDefinitionProvider tools, IActionCommandValidator validator.
        //   Store config (model names, budget weights, max rounds) and service references.
        //   Stateless design: no mutable fields beyond injected dependencies.

        // TODO(impl): PlanReactiveAsync — steps: assemble prompt via IPromptAssembler, call IToolCallingLoop.ExecuteAsync,
        //   validate output via IActionCommandValidator, return ActionCommand[].
        public Task<Action.ActionCommand[]> PlanReactiveAsync(
            GameAlert alert,
            Perception.ColonySnapshot snapshot,
            Memory.WorkingMemory memory,
            CancellationToken ct)
        {
            throw new NotImplementedException();
        }

        // TODO(impl): PlanTacticalAsync — same pattern for tactical tier.
        public Task<Action.ActionCommand[]> PlanTacticalAsync(
            Perception.ColonySnapshot snapshot,
            EpisodicLog recentEvents,
            Memory.WorkingMemory memory,
            CancellationToken ct)
        {
            throw new NotImplementedException();
        }

        // TODO(impl): PlanStrategicAsync — same pattern for strategic tier with longer history window.
        public Task<Action.ActionCommand[]> PlanStrategicAsync(
            Perception.ColonySnapshot snapshot,
            EpisodicLog history,
            Memory.WorkingMemory memory,
            CancellationToken ct)
        {
            throw new NotImplementedException();
        }
    }

    /// <summary>
    /// Immutable config injected by Orchestrator at construction.
    /// </summary>
    public class CognitionEngineConfig
    {
        // TODO(impl): ReactiveModelName — model used for reactive planning, default "deepseek-v4-flash" for low latency
        public string ReactiveModelName { get; init; }

        // TODO(impl): TacticalModelName — model used for tactical planning, default "deepseek-v4-pro"
        public string TacticalModelName { get; init; }

        // TODO(impl): StrategicModelName — model used for strategic planning, default "deepseek-v4-pro"
        public string StrategicModelName { get; init; }

        // TODO(impl): MaxToolCallRounds — prevent infinite tool-calling loops, default 5
        public int MaxToolCallRounds { get; init; } = 5;

        // TODO(impl): MaxCommandsPerPlan — cap commands per planning session, default 20
        public int MaxCommandsPerPlan { get; init; } = 20;

        // TODO(impl): ReactiveBudgetWeight — 10% of daily budget reserved for reactive tier
        public float ReactiveBudgetWeight { get; init; } = 0.1f;

        // TODO(impl): TacticalBudgetWeight — 40% of daily budget reserved for tactical tier
        public float TacticalBudgetWeight { get; init; } = 0.4f;

        // TODO(impl): StrategicBudgetWeight — 50% of daily budget reserved for strategic tier
        public float StrategicBudgetWeight { get; init; } = 0.5f;
    }

    /// <summary>
    /// Episodic log of past events. Wraps Memory.EventLog for Cognition consumption.
    /// Provides filtered access to historical episodes for prompt assembly.
    /// </summary>
    // TODO(impl): EpisodicLog — adapter/facade over Memory.EventLog.
    //   Provides GetRecent(int count), GetByDateRange(fromDay, toDay), GetByType(eventType).
    //   Used by Cognition prompt assembly and planning tiers to inject historical context.
    //   Thread-safe reads from any thread; writes happen only on main thread via Perception.
    public class EpisodicLog
    {
        // TODO(impl): wraps Memory.EventLog instance
    }
}
