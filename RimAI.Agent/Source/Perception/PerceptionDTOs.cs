using System.Collections.Generic;

namespace RimAI.Agent.Perception
{
    // --- ColonySnapshot Root ---
    // TODO(impl): ColonySnapshot — top-level snapshot DTO from Vertex AI pipeline.
    //   Schema version starts at 1; incremented on breaking DTO changes.
    //   Tick from Find.TickManager.TicksAbs; GameDay from GenDate.DayOfQuadrum; TimeOfDay from GenLocalDate.HourFloat.
    //   Season string from GenDate.Quadrum; Pawns capped to 8 colonists + 5 hostiles + 5 animals.
    //   Null fields omitted by JSON serializer (WriteIndented=false, JsonIgnoreCondition.WhenWritingNull).
    public class ColonySnapshot
    {
        public int Version { get; init; }             // TODO(impl): schema version, starts at 1
        public int Tick { get; init; }                // TODO(impl): Find.TickManager.TicksAbs
        public int GameDay { get; init; }             // TODO(impl): GenDate.DayOfQuadrum
        public float TimeOfDay { get; init; }         // TODO(impl): GenLocalDate.HourFloat
        public string Season { get; init; }           // TODO(impl): GenDate.Quadrum.ToString() — "Spring", "Summer", etc.
        public ColonySummary Summary { get; init; }   // TODO(impl): aggregate colony-level stats
        public List<PawnSnapshot> Pawns { get; init; } // TODO(impl): capped to 8+5+5; empty list (never null)
        public List<ResourceSnapshot> Resources { get; init; } // TODO(impl): capped to 30 entries
        public MapSnapshot Map { get; init; }         // TODO(impl): current map terrain/biome/rooms
        public ThreatSnapshot Threats { get; init; }  // TODO(impl): hostile count, fires, breach risk
        public ResearchSnapshot Research { get; init; } // TODO(impl): current project + progress
        public WeatherSnapshot Weather { get; init; } // TODO(impl): current weather conditions
    }

    // --- ColonySummary ---
    // TODO(impl): ColonySummary — aggregate colony-level statistics. All floats are clamped 0–1 where applicable.
    //   ColonistCount, PrisonerCount, GuestCount, AnimalCount, HostileCount from map.mapPawns.
    //   ColonyWealth, TotalWealth from Find.Storyteller; RaidPoints from StorytellerUtility.
    //   ActiveIncidents from Find.Storyteller.incidentQueue; AvgMood/AvgFoodNeed computed over colonists.
    public class ColonySummary
    {
        public int ColonistCount { get; init; }     // TODO(impl): map.mapPawns.FreeColonistsCount
        public int PrisonerCount { get; init; }     // TODO(impl): map.mapPawns.PrisonersOfColonyCount
        public int GuestCount { get; init; }        // TODO(impl): count of non-colonist non-prisoner humanlikes
        public int AnimalCount { get; init; }       // TODO(impl): map.mapPawns.AllPawns count of animals
        public int HostileCount { get; init; }      // TODO(impl): count of hostile pawns on map
        public float ColonyWealth { get; init; }    // TODO(impl): Find.WealthWatcher.WealthItems
        public float TotalWealth { get; init; }     // TODO(impl): Find.WealthWatcher.WealthTotal
        public float RaidPoints { get; init; }      // TODO(impl): StorytellerUtility.DefaultThreatPointsNow
        public List<string> ActiveIncidents { get; init; } // TODO(impl): incident def names from Find.Storyteller.incidentQueue
        public float AvgMood { get; init; }         // TODO(impl): average mood across colonists, 0–1
        public float AvgFoodNeed { get; init; }     // TODO(impl): average food need across colonists, 0–1
    }

    // --- PawnSnapshot + Sub-DTOs ---
    // TODO(impl): PawnSnapshot — per-pawn snapshot. Fields null for inapplicable pawn types (needs for non-sentient, skills for non-colonists).
    //   Id is pawn.ThingID; Name is pawn.LabelShort; KindDef is pawn.kindDef.defName; FactionName from pawn.Faction.
    //   Null fields omitted by JSON serializer to save tokens.
    public class PawnSnapshot
    {
        public string Id { get; init; }             // TODO(impl): pawn.ThingID
        public string Name { get; init; }           // TODO(impl): pawn.LabelShort
        public string KindDef { get; init; }        // TODO(impl): pawn.kindDef.defName
        public string FactionName { get; init; }    // TODO(impl): pawn.Faction?.Name
        public bool IsColonist { get; init; }       // TODO(impl): pawn.IsColonist
        public bool IsPrisoner { get; init; }       // TODO(impl): pawn.IsPrisoner
        public bool IsDowned { get; init; }         // TODO(impl): pawn.Downed
        public bool IsDrafted { get; init; }        // TODO(impl): pawn.Drafted
        public NeedsSnapshot Needs { get; init; }   // TODO(impl): null for non-sentient pawns
        public SkillsSnapshot Skills { get; init; } // TODO(impl): null for non-colonists
        public HealthSnapshot Health { get; init; } // TODO(impl): always populated for living pawns
        public WorkSnapshot Work { get; init; }     // TODO(impl): current job + priority table
        public EquipSnapshot Equipment { get; init; } // TODO(impl): weapon + apparel + armor
        public PositionSnapshot Position { get; init; } // TODO(impl): null if not on local map
    }

    // TODO(impl): NeedsSnapshot — pawn needs from pawn.needs. Values 0–1. TopThoughts are top 5 by intensity.
    public class NeedsSnapshot
    {
        public float Mood { get; init; }            // TODO(impl): pawn.needs.mood.CurLevel
        public float Food { get; init; }            // TODO(impl): pawn.needs.food.CurLevel
        public float Rest { get; init; }            // TODO(impl): pawn.needs.rest.CurLevel
        public float Recreation { get; init; }      // TODO(impl): pawn.needs.joy.CurLevel
        public float Comfort { get; init; }         // TODO(impl): pawn.needs.comfort.CurLevel
        public float Beauty { get; init; }          // TODO(impl): pawn.needs.beauty.CurLevel
        public float Outdoors { get; init; }        // TODO(impl): pawn.needs.outdoors.CurLevel
        public List<ThoughtSnapshot> TopThoughts { get; init; } // TODO(impl): top 5 thoughts by MoodOffset magnitude, sorted desc
    }

    // TODO(impl): ThoughtSnapshot — a single thought/memory from pawn.needs.mood.thoughts.
    public class ThoughtSnapshot
    {
        public string Label { get; init; }          // TODO(impl): thought.LabelCap
        public float MoodOffset { get; init; }      // TODO(impl): thought.MoodOffset()
        public int ExpiresInTicks { get; init; }    // TODO(impl): remaining tick duration if timed
    }

    // TODO(impl): SkillsSnapshot — top 5 skills by level + passion for colonists.
    public class SkillsSnapshot
    {
        public List<SkillSnapshot> Skills { get; init; } // TODO(impl): top 5 by level desc, then passion weight
    }

    // TODO(impl): SkillSnapshot — a single skill entry.
    public class SkillSnapshot
    {
        public string DefName { get; init; }        // TODO(impl): skill.def.defName, e.g. "Shooting"
        public string Passion { get; init; }        // TODO(impl): "None" | "Minor" | "Major" from pawn.skills.GetSkill(def).passion
        public int Level { get; init; }             // TODO(impl): pawn.skills.GetSkill(def).Level
        public float XpSinceLastLevel { get; init; } // TODO(impl): pawn.skills.GetSkill(def).XpSinceLastLevel
    }

    // TODO(impl): HealthSnapshot — pawn health summary from pawn.health.
    public class HealthSnapshot
    {
        public float Consciousness { get; init; }   // TODO(impl): pawn.health.capacities.GetLevel(PawnCapacityDefOf.Consciousness)
        public float Moving { get; init; }          // TODO(impl): pawn.health.capacities.GetLevel(PawnCapacityDefOf.Moving)
        public float Manipulation { get; init; }    // TODO(impl): pawn.health.capacities.GetLevel(PawnCapacityDefOf.Manipulation)
        public float Talking { get; init; }         // TODO(impl): pawn.health.capacities.GetLevel(PawnCapacityDefOf.Talking)
        public float BloodLoss { get; init; }       // TODO(impl): pawn.health.hediffSet.BleedRateTotal
        public float Pain { get; init; }            // TODO(impl): pawn.health.hediffSet.PainTotal
        public int OpenWoundCount { get; init; }    // TODO(impl): count of hediffs where Bleeding > 0
        public int MissingPartCount { get; init; }  // TODO(impl): count of MissingBodyPart hediffs
        public List<InjurySnapshot> TopInjuries { get; init; } // TODO(impl): top 5 injuries by severity desc
    }

    // TODO(impl): InjurySnapshot — a single injury/health condition.
    public class InjurySnapshot
    {
        public string Label { get; init; }          // TODO(impl): hediff.LabelCap
        public string BodyPartLabel { get; init; }  // TODO(impl): hediff.Part.LabelCap
        public float Severity { get; init; }        // TODO(impl): hediff.Severity
        public bool Bleeding { get; init; }         // TODO(impl): hediff.Bleeding
        public bool Infected { get; init; }         // TODO(impl): hediff.Infected
    }

    // TODO(impl): WorkSnapshot — current job and work priorities for a colonist.
    public class WorkSnapshot
    {
        public string CurrentJobDef { get; init; }  // TODO(impl): pawn.CurJob?.def.defName
        public string CurrentTargetLabel { get; init; } // TODO(impl): pawn.CurJob?.targetA.Thing?.LabelCap
        public Dictionary<string, int> Priorities { get; init; } // TODO(impl): workTypeDefName → 0–4 from pawn.workSettings
    }

    // TODO(impl): EquipSnapshot — equipment and apparel summary.
    public class EquipSnapshot
    {
        public string WeaponLabel { get; init; }    // TODO(impl): pawn.equipment.Primary?.LabelCap
        public float WeaponAvgDps { get; init; }    // TODO(impl): pawn.equipment.Primary?.GetStatValue(StatDefOf.RangedWeapon_Cooldown) derived
        public List<string> ApparelLabels { get; init; } // TODO(impl): top 5 apparel items, outer first
        public float ArmorBlunt { get; init; }      // TODO(impl): pawn.GetStatValue(StatDefOf.ArmorRating_Blunt)
        public float ArmorSharp { get; init; }      // TODO(impl): pawn.GetStatValue(StatDefOf.ArmorRating_Sharp)
    }

    // TODO(impl): PositionSnapshot — spatial location on the map.
    public class PositionSnapshot
    {
        public int X { get; init; }                 // TODO(impl): pawn.Position.x
        public int Z { get; init; }                 // TODO(impl): pawn.Position.z
        public string RoomLabel { get; init; }      // TODO(impl): pawn.GetRoom()?.GetRoomRoleLabel()
        public string AreaDef { get; init; }        // TODO(impl): pawn.Map.areaManager?.AllAreas first matching area label
    }

    // --- Resource, Map, Threat, Research, Weather ---
    // TODO(impl): ResourceSnapshot — inventory item from colony stockpile.
    public class ResourceSnapshot
    {
        public string ThingDef { get; init; }       // TODO(impl): thing.def.defName
        public string Category { get; init; }       // TODO(impl): "Food"|"RawResource"|"Manufactured"|"Medicine" — classified by def categories
        public int Count { get; init; }             // TODO(impl): total stack count across all stockpiles
        public bool IsPerishable { get; init; }     // TODO(impl): thing.def.HasComp(typeof(CompRottable))
        public float AverageHitPoints { get; init; } // TODO(impl): average HP across stacks
    }

    // TODO(impl): MapSnapshot — current map terrain, rooms, and hazards.
    public class MapSnapshot
    {
        public string BiomeDef { get; init; }       // TODO(impl): map.Biome.defName
        public string TerrainHash { get; init; }    // TODO(impl): short hash of terrain grid for diff detection
        public int MapSizeX { get; init; }          // TODO(impl): map.Size.x
        public int MapSizeZ { get; init; }          // TODO(impl): map.Size.z
        public int RoofCollapseRisks { get; init; } // TODO(impl): count of roof cells marked for collapse
        public int FireCount { get; init; }         // TODO(impl): count of Fire things on map
        public int FilthScore { get; init; }        // TODO(impl): aggregate filth count/severity across map
        public int PowerNetExcess { get; init; }    // TODO(impl): surplus power in connected grid (watts)
        public List<RoomSnapshot> TopRooms { get; init; } // TODO(impl): top 5 rooms by wealth desc
    }

    // TODO(impl): RoomSnapshot — a single room's stats.
    public class RoomSnapshot
    {
        public string Label { get; init; }          // TODO(impl): room.Role.label
        public int CellCount { get; init; }         // TODO(impl): room.CellCount
        public float Impressiveness { get; init; }  // TODO(impl): room.GetStat(RoomStatDefOf.Impressiveness)
        public float Cleanliness { get; init; }     // TODO(impl): room.GetStat(RoomStatDefOf.Cleanliness)
        public float Temperature { get; init; }     // TODO(impl): room.Temperature
    }

    // TODO(impl): ThreatSnapshot — immediate dangers to the colony.
    public class ThreatSnapshot
    {
        public int HostilePawnsOnMap { get; init; } // TODO(impl): count of hostile pawns on map
        public int DownedColonists { get; init; }   // TODO(impl): count of downed colonists
        public int FiresNearBase { get; init; }     // TODO(impl): count of fires within 30 cells of any colony building
        public int BreachWallCandidates { get; init; } // TODO(impl): walls adjacent to exterior being attacked
        public float NearestHostileDistance { get; init; } // TODO(impl): distance to nearest hostile pawn; float.MaxValue if none
        public List<string> ActiveRaids { get; init; } // TODO(impl): def names of active raid incidents
        public bool AnyMentalBreakRisk { get; init; }  // TODO(impl): any colonist has mental break threshold exceeded
    }

    // TODO(impl): ResearchSnapshot — current research progress.
    public class ResearchSnapshot
    {
        public string CurrentProject { get; init; } // TODO(impl): Find.ResearchManager.currentProj?.defName
        public float CurrentProgress { get; init; } // TODO(impl): Find.ResearchManager.currentProj?.ProgressPercent
        public List<string> CompletedTechs { get; init; } // TODO(impl): last 20 completed research defNames
        public List<string> AvailableTechs { get; init; } // TODO(impl): next 10 unlockable research defNames
    }

    // TODO(impl): WeatherSnapshot — current weather conditions.
    public class WeatherSnapshot
    {
        public string WeatherDef { get; init; }     // TODO(impl): map.weatherManager.curWeather.defName
        public float WindSpeed { get; init; }       // TODO(impl): map.windManager.WindSpeed
        public float OutdoorTemp { get; init; }     // TODO(impl): map.mapTemperature.OutdoorTemp
        public float IndoorTemp { get; init; }      // TODO(impl): map.mapTemperature.IndoorTemp
        public float ToxicFallout { get; init; }    // TODO(impl): map.gameConditionManager.ConditionIsActive(GameConditionDefOf.ToxicFallout) severity
    }

    // --- Perception Events ---
    // TODO(impl): PerceptionEventType — enumeration of event categories the perception layer surfaces.
    //   Harmony hooks (Priority.Last, read-only observers) fire these on main thread via EventBus.
    public enum PerceptionEventType
    {
        Letter,            // TODO(impl): Postfix on Find.LetterStack.ReceiveLetter
        Alert,             // TODO(impl): Postfix on Alert.AlertActive for new alerts only
        RaidDetected,      // TODO(impl): triggered by raid letter/enemy spawn detection
        PawnDied,          // TODO(impl): Postfix on Pawn.Kill
        PawnDowned,        // TODO(impl): Postfix on Pawn_HealthTracker.MakeDowned
        FireStarted,       // TODO(impl): Postfix on GenSpawn.Spawn filtered for Fire thing
        MentalBreak,       // TODO(impl): Postfix on MentalStateHandler.TryStartMentalState
        MedicalEmergency,  // TODO(impl): Prefix on bleed rate threshold check
        ManhunterPack,     // TODO(impl): triggered by manhunter letter/enemy detection
        SiegeDetected,     // TODO(impl): triggered by siege building detection
        Infestation,       // TODO(impl): triggered by infestation spawn
        SnapshotDelta,     // TODO(impl): fired every snapshot with a SnapshotDiff payload
        BudgetExceeded,    // TODO(impl): fired when daily budget threshold crossed
        AutonomyChanged,   // TODO(impl): fired when autonomy level changed via UI
        Error              // TODO(impl): fired for internal errors that don't map to game events
    }

    // TODO(impl): CriticalLevel — severity classification for events.
    //   High (3) forces immediate snapshot regardless of throttle.
    public enum CriticalLevel
    {
        None = 0,   // TODO(impl): informational only
        Low = 1,    // TODO(impl): notable but not urgent
        Medium = 2, // TODO(impl): triggers reactive planning
        High = 3    // TODO(impl): triggers reactive planning + forces immediate snapshot (bypasses throttle)
    }

    // TODO(impl): PerceptionEvent — the event DTO published through EventBus.
    //   SourcePawnId is null for map-level events.
    //   Metadata carries type-specific key-value pairs (e.g. raid faction, injury severity).
    public class PerceptionEvent
    {
        public PerceptionEventType Type { get; init; } // TODO(impl): event classification
        public CriticalLevel Criticality { get; init; } // TODO(impl): urgency level, High forces snapshot
        public int Tick { get; init; }                // TODO(impl): Find.TickManager.TicksAbs at event time
        public float GameHour { get; init; }          // TODO(impl): GenLocalDate.HourFloat at event time
        public string SourcePawnId { get; init; }     // TODO(impl): pawn.ThingID of source, null = map-level
        public string Message { get; init; }          // TODO(impl): human-readable event description
        public Dictionary<string, string> Metadata { get; init; } // TODO(impl): type-specific metadata (faction, severity, location, etc.)
    }

    // --- Snapshot Diff ---
    // TODO(impl): SnapshotDiff — delta between two snapshots. Computed every snapshot cycle.
    //   Forwarded to EventBus as SnapshotDelta event type.
    //   Uses long tick values for chronological overflow safety.
    public class SnapshotDiff
    {
        public int FromTick { get; init; }          // TODO(impl): previous snapshot tick
        public int ToTick { get; init; }            // TODO(impl): current snapshot tick
        public List<string> AddedPawns { get; init; } // TODO(impl): pawn IDs present in current but not previous
        public List<string> RemovedPawns { get; init; } // TODO(impl): pawn IDs present in previous but not current
        public List<PawnDelta> ChangedPawns { get; init; } // TODO(impl): PawnDelta list for changed pawns
        public List<ResourceDelta> ResourceChanges { get; init; } // TODO(impl): ResourceDelta list for count changes
        public bool ThreatLevelChanged { get; init; } // TODO(impl): true if threat snapshot fields differ meaningfully
        public string NarrativeSummary { get; init; } // TODO(impl): 1–2 sentence English summary from SnapshotDiffEngine.Summarize
    }

    // TODO(impl): PawnDelta — changed fields for a single pawn between snapshots.
    public class PawnDelta
    {
        public string PawnId { get; init; }         // TODO(impl): pawn.ThingID
        public List<string> ChangedFields { get; init; } // TODO(impl): e.g. ["Needs.Mood", "Health.BloodLoss", "Position"]
    }

    // TODO(impl): ResourceDelta — resource count change between snapshots.
    public class ResourceDelta
    {
        public string ThingDef { get; init; }       // TODO(impl): thing.def.defName
        public int OldCount { get; init; }          // TODO(impl): count in previous snapshot
        public int NewCount { get; init; }          // TODO(impl): count in current snapshot
    }

    // --- Cadence & Trimming ---
    // TODO(impl): SnapshotCadenceController — throttles snapshot frequency.
    //   GuaranteedIntervalTicks (2500, ≈1 game-hour): snapshot regardless of delta.
    //   MinimumIntervalTicks (250): hard throttle — no two snapshots closer than this.
    //   ShouldSnapshot returns true if enough ticks elapsed since last snapshot OR if forced by urgent event.
    public class SnapshotCadenceController
    {
        public const int GuaranteedIntervalTicks = 2500;  // TODO(impl): ≈1 game-hour, snapshot regardless
        public const int MinimumIntervalTicks = 250;      // TODO(impl): hard throttle

        // TODO(impl): ShouldSnapshot — returns true if (currentTick - lastSnapshotTick >= MinimumIntervalTicks)
        //   AND (currentTick - lastSnapshotTick >= GuaranteedIntervalTicks OR forcePending from urgent event).
        //   Called on main thread by AgentGameComponent.
        public bool ShouldSnapshot(int currentTick)
        {
            throw new System.NotImplementedException();
        }

        // TODO(impl): NotifySnapshotTaken — records tick of last snapshot for throttling.
        public void NotifySnapshotTaken(int tick)
        {
            throw new System.NotImplementedException();
        }

        // TODO(impl): NotifyEvent — urgent events (CriticalLevel.High) force immediate snapshot on next ShouldSnapshot call.
        public void NotifyEvent(PerceptionEvent e)
        {
            throw new System.NotImplementedException();
        }
    }

    // TODO(impl): ColonySnapshotTrimmer — token-budget-aware JSON trimmer.
    //   Trim order (drop first): resources beyond 30 → rooms beyond top 3 → pawns beyond 8 → per-pawn: WorkSnapshot → EquipSnapshot → thoughts beyond 3 → skills beyond 3.
    //   EstimateTokens uses chars/4 heuristic. TrimToBudget progressively removes fields until under budget.
    public static class ColonySnapshotTrimmer
    {
        public const int MaxTokens = 4096;          // TODO(impl): target max tokens for snapshot JSON
        public const int MaxResourceEntries = 30;   // TODO(impl): max resource entries before trimming

        // TODO(impl): TrimToBudget — trims serialized JSON string to fit within tokenBudget.
        //   Drops fields in priority order without throwing. Returns original if already under budget.
        public static string TrimToBudget(string serializedJson, int tokenBudget = MaxTokens)
        {
            throw new System.NotImplementedException();
        }

        // TODO(impl): EstimateTokens — rough token estimate using chars/4 heuristic.
        public static int EstimateTokens(string json)
        {
            throw new System.NotImplementedException();
        }
    }

    // TODO(impl): SnapshotDiffEngine — computes diff between two snapshots and generates narrative summary.
    public static class SnapshotDiffEngine
    {
        // TODO(impl): Compute — identifies added/removed/changed pawns + resource deltas by comparing two snapshots.
        public static SnapshotDiff Compute(ColonySnapshot prev, ColonySnapshot current)
        {
            throw new System.NotImplementedException();
        }

        // TODO(impl): Summarize — generates 1–2 sentence English narrative from diff.
        public static string Summarize(SnapshotDiff diff)
        {
            throw new System.NotImplementedException();
        }
    }
}
