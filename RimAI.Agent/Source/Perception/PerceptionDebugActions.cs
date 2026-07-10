using LudeonTK;
using Newtonsoft.Json;
using Verse;

namespace RimAI.Agent.Perception
{
    /// <summary>
    /// Developer-only diagnostics for validating the live Perception pipeline.
    /// </summary>
    public static class PerceptionDebugActions
    {
        [DebugAction("RimAI", "Dump ColonySnapshot JSON",
            allowedGameStates = AllowedGameStates.PlayingOnMap)]
        public static void DumpColonySnapshotJson()
        {
            var snapshot = new SnapshotBuilder().TakeSnapshot(null);
            var json = JsonConvert.SerializeObject(
                snapshot,
                ColonySnapshotTrimmer.SerializerSettings);

            Log.Message("[RimAI] ColonySnapshot JSON: " + json);
        }
    }
}
