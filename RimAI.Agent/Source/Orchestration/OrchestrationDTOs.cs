using System.Collections.Generic;

namespace RimAI.Agent.Orchestration
{
    /// <summary>
    /// Tier enum used by CadenceScheduler, BudgetManager, and DecisionLogger.
    /// Mirrors Cognition.PlanningTier for orchestration-level operations.
    /// </summary>
    public enum CognitionTier
    {
        // TODO(impl): Reactive — event-driven, <1 game-hour scope, fires on critical events
        Reactive,
        // TODO(impl): Tactical — scheduled ~every 6 game-hours, work/bill/zone/designation ops
        Tactical,
        // TODO(impl): Strategic — scheduled ~every 2 game-days, research/trade/caravan/policy ops
        Strategic
    }

    /// <summary>
    /// Autonomy level controlling how much freedom the AI has to execute commands.
    /// Advisor: suggest only, no auto-execute. Copilot: safe/neutral auto-execute, destructive confirm.
    /// Full: all commands auto-execute without confirmation.
    /// </summary>
    public enum AutonomyLevel
    {
        // TODO(impl): Advisor — suggests commands only; no auto-execution; all actions require player approval
        Advisor,
        // TODO(impl): Copilot — safe + neutral commands auto-execute; destructive require confirmation dialog (30s timeout, default reject)
        Copilot,
        // TODO(impl): Full — all commands auto-execute without confirmation; unattended colony management
        Full
    }

    /// <summary>
    /// Budget configuration DTO. Controls per-day token and API call limits
    /// with tier-specific reserve percentages.
    /// </summary>
    public class BudgetConfig
    {
        // TODO(impl): DailyTokenBudget — max tokens per game-day, default 200,000
        public int DailyTokenBudget { get; init; } = 200_000;

        // TODO(impl): DailyCallBudget — max API calls per game-day, default 100
        public int DailyCallBudget { get; init; } = 100;

        // TODO(impl): ReactiveReserve — 10% of budget reserved for reactive tier (survives longest)
        public float ReactiveReserve { get; init; } = 0.10f;

        // TODO(impl): TacticalReserve — 40% of budget reserved for tactical tier
        public float TacticalReserve { get; init; } = 0.40f;

        // TODO(impl): StrategicReserve — 50% of budget reserved for strategic tier (deactivates first)
        public float StrategicReserve { get; init; } = 0.50f;
    }

    /// <summary>
    /// Current budget consumption state. Persisted via ExposeData.
    /// Reset daily at midnight game-time.
    /// </summary>
    public class BudgetState
    {
        // TODO(impl): TokensUsedToday — cumulative tokens consumed today across all tiers
        public int TokensUsedToday { get; init; }

        // TODO(impl): CallsUsedToday — cumulative API calls made today across all tiers
        public int CallsUsedToday { get; init; }

        // TODO(impl): LastResetDay — game day when counters were last reset (for midnight detection)
        public int LastResetDay { get; init; }

        // TODO(impl): ActiveTiers — set of CognitionTier values currently active.
        //   Degradation removes tiers as budget depletes: Strategic at 20%, Tactical at 5%.
        public HashSet<CognitionTier> ActiveTiers { get; init; }
    }

    /// <summary>
    /// Cadence timing state. Persisted via ExposeData.
    /// Tracks last-fire ticks for each tier to enforce scheduling and cooldowns.
    /// </summary>
    public class CadenceState
    {
        // TODO(impl): LastReactiveTick — tick when reactive planner last fired
        public int LastReactiveTick { get; init; }

        // TODO(impl): LastTacticalTick — tick when tactical planner last fired
        public int LastTacticalTick { get; init; }

        // TODO(impl): LastStrategicTick — tick when strategic planner last fired
        public int LastStrategicTick { get; init; }

        // TODO(impl): MinimumSpacingTicks — minimum ticks between ANY tier firing, default 5000 (~0.5 game-hours).
        //   Prevents cognition storming when multiple tiers are due simultaneously.
        public int MinimumSpacingTicks { get; init; } = 5000;

        // TODO(impl): PendingReactiveAlert — stored GameAlert for next reactive fire; cleared after MarkFired
        public Cognition.GameAlert PendingReactiveAlert { get; init; }
    }

    /// <summary>
    /// Single entry in the decision log ring buffer.
    /// Records every AI-generated command with its execution outcome.
    /// </summary>
    public class DecisionLogEntry
    {
        // TODO(impl): Tick — RimWorld tick when command was executed
        public int Tick { get; init; }

        // TODO(impl): GameTime — game-days fraction when command was executed
        public float GameTime { get; init; }

        // TODO(impl): Tier — which cognition tier generated this command
        public CognitionTier Tier { get; init; }

        // TODO(impl): CommandType — the ActionCommandType of the executed command
        public Action.ActionCommandType CommandType { get; init; }

        // TODO(impl): Parameters — truncated JSON representation of command parameters, max 200 chars
        public string Parameters { get; init; }

        // TODO(impl): Reason — planner reasoning from ActionCommand.Reason, truncated to 200 chars
        public string Reason { get; init; }

        // TODO(impl): Success — true if command executed successfully (set after execution completes)
        public bool Success { get; init; }

        // TODO(impl): TokensUsed — tokens consumed by the LLM call that generated this command
        public int TokensUsed { get; init; }
    }
}
