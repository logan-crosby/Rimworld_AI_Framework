using System.Collections.Generic;
using HarmonyLib;
using RimWorld;
using Verse;

namespace RimAI.Agent.Perception
{
    /// <summary>
    /// Harmony patch classes — all Priority.Last, read-only observers.
    /// Each postfix/prefix constructs a PerceptionEvent and publishes it via
    /// EventBus.PublishDeferred (thread-safe for off-thread callbacks).
    ///
    /// Compile-only at this phase — hook bodies demonstrate the event-construction
    /// pattern. Full runtime validation happens during integration testing.
    ///
    /// G-24: events tagged with mapId via Metadata["mapId"].
    /// G-24: hooks use Map (from event context), never Find.CurrentMap.
    /// </summary>

    // ── Helpers ────────────────────────────────────────────────────────────

    /// <summary>Shared factory and mapId convention for hook classes.</summary>
    internal static class HookHelper
    {
        public const string MetadataKeyMapId = "mapId";

        private static float SafeGameHour(Map map)
        {
            if (map != null) return GenLocalDate.HourFloat(map);
            var ticks = Find.TickManager?.TicksAbs ?? 0;
            return GenDate.HourFloat(ticks, 0f);
        }

        private static int SafeTick()
        {
            return Find.TickManager?.TicksAbs ?? 0;
        }

        public static PerceptionEvent BuildEvent(
            PerceptionEventType type,
            CriticalLevel criticality,
            Map map,
            string message,
            string sourcePawnId = null,
            Dictionary<string, string> extraMeta = null)
        {
            var meta = extraMeta ?? new Dictionary<string, string>();
            if (map != null)
                meta[MetadataKeyMapId] = map.uniqueID.ToString();

            return new PerceptionEvent
            {
                Type = type,
                Criticality = criticality,
                Tick = SafeTick(),
                GameHour = SafeGameHour(map),
                SourcePawnId = sourcePawnId,
                Message = message,
                Metadata = meta
            };
        }
    }

    // ── Hook 1: Letters (§4.3) ─────────────────────────────────────────────

    /// <summary>
    /// Postfix on LetterStack.ReceiveLetter — fires on every letter/notification.
    /// Classifies raid/manhunter/siege/infestation letters.
    /// </summary>
    [HarmonyPatch(typeof(LetterStack), nameof(LetterStack.ReceiveLetter))]
    public static class Hook_LetterReceived
    {
        [HarmonyPriority(Priority.Last)]
        public static void Postfix(Letter letter)
        {
            if (letter == null) return;

            var map = letter.lookTargets?.PrimaryTarget.Thing?.Map;
            var defName = letter.def?.defName ?? string.Empty;
            var labelStr = letter.Label.ToString();
            var message = string.IsNullOrEmpty(labelStr) ? defName : labelStr;

            var evt = HookHelper.BuildEvent(
                type: ClassifyLetterType(defName),
                criticality: ClassifyLetterCriticality(defName),
                map: map,
                message: message,
                extraMeta: new Dictionary<string, string>
                {
                    { "defName", defName },
                    { "label", labelStr }
                });

            EventBus.PublishDeferred(evt);
        }

        private static PerceptionEventType ClassifyLetterType(string defName)
        {
            if (defName.Contains("Raid")) return PerceptionEventType.RaidDetected;
            if (defName.Contains("Manhunter")) return PerceptionEventType.ManhunterPack;
            if (defName.Contains("Siege")) return PerceptionEventType.SiegeDetected;
            if (defName.Contains("Infestation")) return PerceptionEventType.Infestation;
            return PerceptionEventType.Letter;
        }

        private static CriticalLevel ClassifyLetterCriticality(string defName)
        {
            if (defName.Contains("ThreatBig") || defName.Contains("Raid") ||
                defName.Contains("Manhunter") || defName.Contains("Siege") ||
                defName.Contains("Infestation"))
                return CriticalLevel.High;
            if (defName.Contains("ThreatSmall") || defName.Contains("NegativeEvent"))
                return CriticalLevel.Medium;
            return CriticalLevel.Low;
        }
    }

    // ── Hook 2: Alerts (§4.3) ──────────────────────────────────────────────

    /// <summary>
    /// Postfix on Alert.get_Active — fires whenever an alert's active state is read.
    /// Note: spec §4.3 targets "Alert.AlertActive (new only)", but the RimWorld API
    /// exposes alert state via the Active property. This hook monitors that.
    /// </summary>
    [HarmonyPatch(typeof(Alert), "get_Active")]
    public static class Hook_AlertActivated
    {
        [HarmonyPriority(Priority.Last)]
        public static void Postfix(Alert __instance, bool __result)
        {
            if (__instance == null || !__result) return;

            var evt = new PerceptionEvent
            {
                Type = PerceptionEventType.Alert,
                Criticality = __instance.Priority == AlertPriority.Critical
                    ? CriticalLevel.High : CriticalLevel.Low,
                Tick = Find.TickManager?.TicksAbs ?? 0,
                GameHour = GenDate.HourFloat(Find.TickManager?.TicksAbs ?? 0, 0f),
                Message = __instance.GetLabel() ?? __instance.GetType().Name,
                Metadata = new Dictionary<string, string>
                {
                    { "alertClass", __instance.GetType().Name }
                }
            };

            EventBus.PublishDeferred(evt);
        }
    }

    // ── Hook 3: Pawn death (§4.3) ──────────────────────────────────────────

    [HarmonyPatch(typeof(Pawn), nameof(Pawn.Kill))]
    public static class Hook_PawnKilled
    {
        [HarmonyPriority(Priority.Last)]
        public static void Postfix(Pawn __instance, DamageInfo? dinfo)
        {
            if (__instance == null) return;

            var evt = HookHelper.BuildEvent(
                type: PerceptionEventType.PawnDied,
                criticality: __instance.IsColonist ? CriticalLevel.High : CriticalLevel.Medium,
                map: __instance.Map,
                message: $"{__instance.LabelShortCap} died",
                sourcePawnId: __instance.GetUniqueLoadID(),
                extraMeta: new Dictionary<string, string>
                {
                    { "pawnDef", __instance.def?.defName ?? string.Empty },
                    { "isColonist", __instance.IsColonist.ToString() },
                    { "isHostile", __instance.HostileTo(Faction.OfPlayer).ToString() }
                });

            EventBus.PublishDeferred(evt);
        }
    }

    // ── Hook 4: Pawn downed (§4.3) ─────────────────────────────────────────

    /// <summary>
    /// Postfix on Pawn_HealthTracker.MakeDowned.
    /// Access the parent Pawn via Traverse since the pawn field is private.
    /// </summary>
    [HarmonyPatch(typeof(Pawn_HealthTracker), "MakeDowned")]
    public static class Hook_PawnDowned
    {
        [HarmonyPriority(Priority.Last)]
        public static void Postfix(Pawn_HealthTracker __instance)
        {
            if (__instance == null) return;

            var pawn = Traverse.Create(__instance).Field("pawn").GetValue<Pawn>();
            if (pawn == null) return;

            var evt = HookHelper.BuildEvent(
                type: PerceptionEventType.PawnDowned,
                criticality: pawn.IsColonist ? CriticalLevel.High : CriticalLevel.Medium,
                map: pawn.Map,
                message: $"{pawn.LabelShortCap} was downed",
                sourcePawnId: pawn.GetUniqueLoadID(),
                extraMeta: new Dictionary<string, string>
                {
                    { "pawnDef", pawn.def?.defName ?? string.Empty },
                    { "isColonist", pawn.IsColonist.ToString() }
                });

            EventBus.PublishDeferred(evt);
        }
    }

    // ── Hook 5: Fire started (§4.3) ────────────────────────────────────────

    /// <summary>
    /// Postfix on GenSpawn.Spawn(Thing, IntVec3, Map, Rot4, WipeMode, bool).
    /// Filters for Fire things only.
    /// </summary>
    [HarmonyPatch(typeof(GenSpawn), nameof(GenSpawn.Spawn),
        typeof(Thing), typeof(IntVec3), typeof(Map), typeof(Rot4), typeof(WipeMode), typeof(bool))]
    public static class Hook_FireSpawned
    {
        [HarmonyPriority(Priority.Last)]
        public static void Postfix(Thing newThing, Map map)
        {
            if (newThing is not Fire fire) return;
            if (map == null) return;

            var evt = HookHelper.BuildEvent(
                type: PerceptionEventType.FireStarted,
                criticality: CriticalLevel.Medium,
                map: map,
                message: $"Fire started at {fire.Position}",
                extraMeta: new Dictionary<string, string>
                {
                    { "fireSize", fire.fireSize.ToString("F2") },
                    { "position", fire.Position.ToString() }
                });

            EventBus.PublishDeferred(evt);
        }
    }

    // ── Hook 6: Mental break (§4.3) ────────────────────────────────────────

    /// <summary>
    /// Postfix on MentalStateHandler.TryStartMentalState.
    /// Uses string-based Harmony target for compatibility across RW versions.
    /// </summary>
    [HarmonyPatch("RimWorld.MentalStateHandler", "TryStartMentalState")]
    public static class Hook_MentalBreak
    {
        [HarmonyPriority(Priority.Last)]
        public static void Postfix(object __instance, MentalStateDef stateDef, bool __result)
        {
            if (!__result) return;
            if (__instance == null || stateDef == null) return;

            // Access pawn via Traverse since we can't type __instance
            var pawn = Traverse.Create(__instance).Field("pawn").GetValue<Pawn>();
            if (pawn == null) return;

            var stateLabel = stateDef.label?.ToString() ?? string.Empty;

            var evt = HookHelper.BuildEvent(
                type: PerceptionEventType.MentalBreak,
                criticality: CriticalLevel.High,
                map: pawn.Map,
                message: $"{pawn.LabelShortCap} has a mental break: {stateLabel}",
                sourcePawnId: pawn.GetUniqueLoadID(),
                extraMeta: new Dictionary<string, string>
                {
                    { "stateDef", stateDef.defName },
                    { "stateLabel", stateLabel },
                    { "isColonist", pawn.IsColonist.ToString() }
                });

            EventBus.PublishDeferred(evt);
        }
    }

    // ── Hook 7: Medical emergency (§4.3) ────────────────────────────────────

    /// <summary>
    /// Prefix on Pawn_HealthTracker.HealthTick — checks bleed rate before
    /// the tick executes. Fires when total bleed rate exceeds emergency threshold.
    /// </summary>
    [HarmonyPatch(typeof(Pawn_HealthTracker), "HealthTick")]
    public static class Hook_MedicalEmergency
    {
        private const float BleedEmergencyThreshold = 0.30f;

        [HarmonyPriority(Priority.Last)]
        public static void Prefix(Pawn_HealthTracker __instance)
        {
            if (__instance == null) return;

            var pawn = Traverse.Create(__instance).Field("pawn").GetValue<Pawn>();
            if (pawn == null) return;
            if (!pawn.IsColonist && !pawn.IsPrisonerOfColony) return;

            var bleedRate = __instance.hediffSet?.BleedRateTotal ?? 0f;
            if (bleedRate < BleedEmergencyThreshold) return;

            var evt = HookHelper.BuildEvent(
                type: PerceptionEventType.MedicalEmergency,
                criticality: CriticalLevel.High,
                map: pawn.Map,
                message: $"{pawn.LabelShortCap} is bleeding out (rate: {bleedRate:F2}/day)",
                sourcePawnId: pawn.GetUniqueLoadID(),
                extraMeta: new Dictionary<string, string>
                {
                    { "bleedRate", bleedRate.ToString("F3") },
                    { "isColonist", pawn.IsColonist.ToString() }
                });

            EventBus.PublishDeferred(evt);
        }
    }
}
