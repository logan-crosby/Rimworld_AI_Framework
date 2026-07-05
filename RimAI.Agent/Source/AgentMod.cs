using Verse;

namespace RimAI.Agent
{
    /// <summary>RimAI Agent mod entry point. Registers GameComponent on startup.</summary>
    public class AgentMod : Mod
    {
        public static AgentMod Instance { get; private set; }

        public AgentMod(ModContentPack content) : base(content)
        {
            Instance = this;
        }

        public override string SettingsCategory() => "RimAI Agent";

        // TODO(impl): hook settings window — wire up autonomy controls (sliders, toggles, dropdowns)
        // TODO(impl): hook autonomy controls — connect AutonomyLevel dropdown, budget sliders, reserve percentages
        // TODO(impl): register AgentGameComponent on game start via Current.Game.components.Add
        // TODO(impl): wire decision log viewer UI, export-to-file button
    }
}
