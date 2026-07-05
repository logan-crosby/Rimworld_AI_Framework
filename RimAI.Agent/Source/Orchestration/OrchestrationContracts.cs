using System.Collections.Generic;
using RimAI.Agent.Action;

namespace RimAI.Agent.Orchestration
{
    /// <summary>
    /// Top-level orchestrator. Owns the full AI loop lifecycle (start/pause/resume/shutdown).
    /// Exposes autonomy level, budget config, budget state, and decision log.
    /// </summary>
    public interface IOrchestrator
    {
        // TODO(impl): Start — begins the AI loop. Initializes all layers, wires EventBus subscriptions,
        //   restores cadence from ExposeData, begins GameComponent tick processing.
        void Start();

        // TODO(impl): Pause — cancels in-flight Tasks (CancellationTokenSource.Cancel),
        //   clears pending action command queue, preserves all state for resume.
        void Pause();

        // TODO(impl): Resume — restores cadence from saved state, no retroactive catch-up.
        //   Resumes GameComponent tick processing from current game tick.
        void Resume();

        // TODO(impl): Shutdown — cancels all Tasks, disposes CancellationTokenSources,
        //   nulls static refs for clean mod unload. Called on mod deactivation.
        void Shutdown();

        // TODO(impl): IsRunning — true if the AI loop is actively processing ticks
        bool IsRunning { get; }

        // TODO(impl): AutonomyLevel — get/set current autonomy level; setting triggers
        //   safety guardrail re-evaluation and AutonomyChanged EventBus event
        AutonomyLevel AutonomyLevel { get; set; }

        // TODO(impl): Budget — get/set current budget config; setting triggers
        //   IBudgetManager.ApplyConfig() and degradation re-evaluation
        BudgetConfig Budget { get; set; }

        // TODO(impl): BudgetState — read-only current budget consumption state
        BudgetState BudgetState { get; }

        // TODO(impl): RecentDecisions — returns last N decision log entries,
        //   optionally filtered by CognitionTier (null = all tiers)
        IReadOnlyList<DecisionLogEntry> RecentDecisions(int count, CognitionTier? tier);

        // TODO(impl): ForceTrigger — force-immediate planning for a tier.
        //   Reactive requires GameAlert context; Tactical/Strategic fire on next tick.
        //   Bypasses cadence cooldowns but still subject to budget CanAfford check.
        void ForceTrigger(CognitionTier tier, Cognition.GameAlert alert = null);
    }

    /// <summary>
    /// Controls when each cognition tier fires based on game-tick cadence.
    /// Reactive is event-driven; Tactical fires ~6h; Strategic fires ~2d.
    /// </summary>
    public interface ICadenceScheduler
    {
        // TODO(impl): ShouldFireReactive — returns true if a pending reactive trigger exists
        //   and minimum spacing (5000 ticks) since last reactive has elapsed.
        //   Reactive always wins priority over tactical/strategic on same tick.
        bool ShouldFireReactive();

        // TODO(impl): ShouldFireTactical — returns true if 60,000 ticks (~6 game-hours)
        //   have elapsed since last tactical fire, AND minimum spacing since any tier.
        bool ShouldFireTactical();

        // TODO(impl): ShouldFireStrategic — returns true if 120,000 ticks (~2 game-days)
        //   have elapsed since last strategic fire, AND minimum spacing since any tier.
        bool ShouldFireStrategic();

        // TODO(impl): MarkFired — records the current tick as the last-fire tick for the given tier.
        //   Called by Orchestrator after successfully launching a planner for that tier.
        void MarkFired(CognitionTier tier);

        // TODO(impl): ForceTriggerReactive — flags reactive as due immediately with alert context.
        //   Called by EventBus subscriber when CriticalLevel >= Medium events arrive.
        void ForceTriggerReactive(Cognition.GameAlert alert);

        // TODO(impl): ForceTriggerTactical — flags tactical as due on next ShouldFireTactical() check.
        void ForceTriggerTactical();

        // TODO(impl): ForceTriggerStrategic — flags strategic as due on next ShouldFireStrategic() check.
        void ForceTriggerStrategic();

        // TODO(impl): ResetAllCooldowns — resets all last-fire ticks to 0.
        //   Used on AI resume or manual reset via dev tools.
        void ResetAllCooldowns();

        // TODO(impl): GetState — returns current CadenceState snapshot for UI display and persistence.
        CadenceState GetState();
    }

    /// <summary>
    /// Enforces per-day token and API call budgets with tier degradation.
    /// Strategic deactivates first at 20% remaining; Reactive survives longest.
    /// </summary>
    public interface IBudgetManager
    {
        // TODO(impl): CanAfford — returns true if the tier can proceed given estimatedTokens.
        //   Checks: (1) tier is active per degradation rules, (2) remaining budget >= estimatedTokens,
        //   (3) remaining calls >= 1. If not, returns false.
        //   Overage grace: in-flight call that exceeds remaining completes and charges the overage.
        bool CanAfford(CognitionTier tier, int estimatedTokens);

        // TODO(impl): Deduct — subtracts actualTokens from remaining budget after LLM call completes.
        //   Also increments calls counter. Re-evaluates degradation tiers.
        void Deduct(CognitionTier tier, int actualTokens);

        // TODO(impl): GetState — returns current BudgetState for UI display and persistence.
        BudgetState GetState();

        // TODO(impl): ApplyConfig — replaces current budget config; re-evaluates degradation tiers.
        //   TokensUsedToday is NOT retroactive; only affects future CanAfford checks.
        void ApplyConfig(BudgetConfig config);

        // TODO(impl): ResetDaily — resets TokensUsedToday and CallsUsedToday to 0.
        //   Called when gameDay >= LastResetDay + 1 (midnight game-time).
        //   Re-evaluates active tiers after reset.
        void ResetDaily();
    }

    /// <summary>
    /// Controls command execution per autonomy level.
    /// Blocks/suggests/confirms destructive actions based on Advisor/Copilot/Full modes.
    /// </summary>
    public interface ISafetyGuardrail
    {
        // TODO(impl): AllowsCommand — returns true if the command is allowed at current autonomy level.
        //   Advisor: only safe commands (suggest only); destructive blocked.
        //   Copilot: safe + neutral auto-execute; destructive require confirmation dialog.
        //   Full: all commands allowed without confirmation.
        bool AllowsCommand(ActionCommand command);

        // TODO(impl): AllowsAdvancedAction — convenience check for a specific ActionCommandType.
        bool AllowsAdvancedAction(ActionCommandType type);

        // TODO(impl): SetAutonomyLevel — changes current level; flushes pending destructive commands
        //   from queue if transitioning from Full to Copilot/Advisor.
        void SetAutonomyLevel(AutonomyLevel level);

        // TODO(impl): CurrentLevel — read current autonomy level
        AutonomyLevel CurrentLevel { get; }

        // TODO(impl): RequiresConfirmation — returns true if this command type requires
        //   a confirmation dialog at current autonomy level (relevant in Copilot mode).
        bool RequiresConfirmation(ActionCommandType type);
    }

    /// <summary>
    /// Records every AI-generated command with execution outcome.
    /// Ring buffer with configurable capacity; persisted via ExposeData.
    /// </summary>
    public interface IDecisionLogger
    {
        // TODO(impl): Log — appends a DecisionLogEntry to the ring buffer.
        //   If buffer is full (default 500), oldest entry is silently evicted.
        //   Called on main thread after command execution completes.
        void Log(DecisionLogEntry entry);

        // TODO(impl): GetRecentDecisions — returns last N entries, newest first.
        //   Optionally filtered by CognitionTier (null = all tiers).
        IReadOnlyList<DecisionLogEntry> GetRecentDecisions(int count = 20, CognitionTier? tier = null);

        // TODO(impl): GetDecisionsSince — returns all entries since the given game tick.
        IReadOnlyList<DecisionLogEntry> GetDecisionsSince(int tick);

        // TODO(impl): ExportToFile — writes recent entries as formatted text to a file path.
        //   Used by dev console command "RimAI.DumpDecisionLog".
        void ExportToFile(string path);
    }
}
