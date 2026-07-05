namespace RimAI.Agent.UI
{
    /// <summary>
    /// Settings UI tab stub for the RimAI Agent mod settings panel.
    /// Provides autonomy controls, budget panel, and decision log viewer.
    /// </summary>
    public static class AdminTab
    {
        // TODO(impl): AdminTab — settings UI tab accessed via Mod.SettingsCategory in RimWorld mod settings.
        //   Controls:
        //     - AI Enable/Disable toggle (confirmation dialog)
        //     - Autonomy Level dropdown: Advisor / Copilot / Full (default Copilot)
        //     - Daily Token Budget slider: 10K–1M step 5K (default 200K)
        //     - Daily Call Budget slider: 10–500 step 5 (default 100)
        //     - Strategic Reserve % slider: 10–70 (default 50)
        //     - Tactical Reserve % slider: 10–60 (default 40)
        //     - Reactive Reserve % slider: 5–30 (default 10)
        //     - Decision Log Size slider: 100–2000 (default 500)
        //   Read-only display:
        //     - Current budget state: tokens used / limit, calls used / limit
        //     - Current game-day, active tiers with paused/degraded rationale
        //   Decision log viewer:
        //     - Scrollable list, newest-first
        //     - Filter by tier dropdown: All / Reactive / Tactical / Strategic
        //     - Color-coded: green = success, red = rejected/failed
        //     - "Export to File" button → IDecisionLogger.ExportToFile
        //   Pause AI button:
        //     - Cancels in-flight Tasks, clears pending commands, preserves state
        //     - Resume restores cadence from save, no retroactive catch-up
        //   All controls wired to IOrchestrator properties (AutonomyLevel, Budget, etc.)

        // TODO(impl): RenderSettings — override listing_standard.Begin/End, draw all controls
        // TODO(impl): SaveSettings — write current values back to IOrchestrator.Budget, etc.
        // TODO(impl): ExposeData — persist UI settings (last autonomy level, budget config)
    }
}
