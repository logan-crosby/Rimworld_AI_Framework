using System.Collections.Generic;

namespace RimAI.Agent.Perception
{
    // =========================================================================
    // ColonySnapshot Root — schema v2 (G-02 entity additions)
    // Token budget: ~5K, ceiling 5,500.
    // Serialized as minified JSON; null fields omitted.
    // =========================================================================

    /// <summary>
    /// Top-level colony snapshot DTO. Serialized as minified JSON for LLM context.
    /// Version 2 adds G-02 entity types: Buildings, Zones, Bills, NotableItems,
    /// Policies, Factions.
    /// </summary>
    public class ColonySnapshot
    {
        public int Version { get; set; } = 2;
        public int Tick { get; set; }
        public int GameDay { get; set; }
        public float TimeOfDay { get; set; }
        public string Season { get; set; }

        public ColonySummary Summary { get; set; }

        /// <summary>Pawns on the map. Builder caps at 8 colonists + 5 hostiles + 5 animals.</summary>
        public List<PawnSnapshot> Pawns { get; set; }

        /// <summary>Colony inventory. Builder caps at 30 entries.</summary>
        public List<ResourceSnapshot> Resources { get; set; }

        public MapSnapshot Map { get; set; }
        public ThreatSnapshot Threats { get; set; }
        public ResearchSnapshot Research { get; set; }
        public WeatherSnapshot Weather { get; set; }

        // ── G-02 additions (schema v2) ──────────────────────────────────────

        /// <summary>Production/comms/power buildings. Builder caps at 20.</summary>
        public List<BuildingSnapshot> Buildings { get; set; }

        /// <summary>Growing zones, stockpiles, dumping zones.</summary>
        public List<ZoneSnapshot> Zones { get; set; }

        /// <summary>Active bills on production tables. Builder caps at 20.</summary>
        public List<BillSnapshot> Bills { get; set; }

        /// <summary>Notable items on the ground (weapons, apparel, medicine). Builder caps at 15.</summary>
        public List<ItemSnapshot> NotableItems { get; set; }

        /// <summary>Available outfit, drug, and food policy names.</summary>
        public PolicySnapshot Policies { get; set; }

        /// <summary>Known factions. Builder caps at 8 (player faction excluded).</summary>
        public List<FactionSnapshot> Factions { get; set; }
    }

    // =========================================================================
    // ColonySummary
    // =========================================================================

    /// <summary>Aggregate colony-level statistics. Mood/food values are 0–1.</summary>
    public class ColonySummary
    {
        public int ColonistCount { get; set; }
        public int PrisonerCount { get; set; }
        public int GuestCount { get; set; }
        public int AnimalCount { get; set; }
        public int HostileCount { get; set; }
        public float ColonyWealth { get; set; }
        public float TotalWealth { get; set; }
        public float RaidPoints { get; set; }
        public List<string> ActiveIncidents { get; set; }
        public float AvgMood { get; set; }
        public float AvgFoodNeed { get; set; }
    }

    // =========================================================================
    // PawnSnapshot + Sub-DTOs
    // =========================================================================

    /// <summary>
    /// Per-pawn snapshot. EntityId uses Thing.GetUniqueLoadID() per G-03 contract.
    /// Sub-DTOs are null for inapplicable pawn types (needs for non-sentient,
    /// skills for non-colonists). Null fields omitted by JSON serializer.
    /// </summary>
    public class PawnSnapshot
    {
        /// <summary>Save-stable entity ID per G-03: pawn.GetUniqueLoadID().</summary>
        public string EntityId { get; set; }

        /// <summary>Human-readable label: pawn.LabelShort.</summary>
        public string Label { get; set; }

        public string KindDef { get; set; }
        public string FactionName { get; set; }
        public bool IsColonist { get; set; }
        public bool IsPrisoner { get; set; }
        public bool IsDowned { get; set; }
        public bool IsDrafted { get; set; }

        /// <summary>Null for non-sentient pawns.</summary>
        public NeedsSnapshot Needs { get; set; }

        /// <summary>Null for non-colonists.</summary>
        public SkillsSnapshot Skills { get; set; }

        /// <summary>Always populated for living pawns.</summary>
        public HealthSnapshot Health { get; set; }

        public WorkSnapshot Work { get; set; }
        public EquipSnapshot Equipment { get; set; }

        /// <summary>Null if not on local map.</summary>
        public PositionSnapshot Position { get; set; }
    }

    /// <summary>Pawn needs. Values 0–1. TopThoughts are top 5 by intensity.</summary>
    public class NeedsSnapshot
    {
        public float Mood { get; set; }
        public float Food { get; set; }
        public float Rest { get; set; }
        public float Recreation { get; set; }
        public float Comfort { get; set; }
        public float Beauty { get; set; }
        public float Outdoors { get; set; }
        public List<ThoughtSnapshot> TopThoughts { get; set; }
    }

    /// <summary>A single thought/memory from pawn.needs.mood.thoughts.</summary>
    public class ThoughtSnapshot
    {
        public string Label { get; set; }
        public float MoodOffset { get; set; }
        public int ExpiresInTicks { get; set; }
    }

    /// <summary>Top 5 skills by level + passion for colonists.</summary>
    public class SkillsSnapshot
    {
        public List<SkillSnapshot> Skills { get; set; }
    }

    /// <summary>A single skill entry.</summary>
    public class SkillSnapshot
    {
        /// <summary>e.g. "Shooting".</summary>
        public string DefName { get; set; }

        /// <summary>"None", "Minor", or "Major".</summary>
        public string Passion { get; set; }

        public int Level { get; set; }
        public float XpSinceLastLevel { get; set; }
    }

    /// <summary>Pawn health summary from pawn.health.</summary>
    public class HealthSnapshot
    {
        public float Consciousness { get; set; }
        public float Moving { get; set; }
        public float Manipulation { get; set; }
        public float Talking { get; set; }
        public float BloodLoss { get; set; }
        public float Pain { get; set; }
        public int OpenWoundCount { get; set; }
        public int MissingPartCount { get; set; }

        /// <summary>Top 5 injuries by severity desc.</summary>
        public List<InjurySnapshot> TopInjuries { get; set; }
    }

    /// <summary>A single injury/health condition.</summary>
    public class InjurySnapshot
    {
        public string Label { get; set; }
        public string BodyPartLabel { get; set; }
        public float Severity { get; set; }
        public bool Bleeding { get; set; }
        public bool Infected { get; set; }
    }

    /// <summary>Current job and work priorities for a colonist.</summary>
    public class WorkSnapshot
    {
        public string CurrentJobDef { get; set; }
        public string CurrentTargetLabel { get; set; }

        /// <summary>workTypeDefName → 0–4 priority.</summary>
        public Dictionary<string, int> Priorities { get; set; }
    }

    /// <summary>Equipment and apparel summary.</summary>
    public class EquipSnapshot
    {
        public string WeaponLabel { get; set; }
        public float WeaponAvgDps { get; set; }

        /// <summary>Top 5 apparel items, outer first.</summary>
        public List<string> ApparelLabels { get; set; }

        public float ArmorBlunt { get; set; }
        public float ArmorSharp { get; set; }
    }

    /// <summary>Spatial location on the map.</summary>
    public class PositionSnapshot
    {
        public int X { get; set; }
        public int Z { get; set; }
        public string RoomLabel { get; set; }
        public string AreaDef { get; set; }
    }

    // =========================================================================
    // Resource, Map, Threat, Research, Weather
    // =========================================================================

    /// <summary>Inventory item from colony stockpile.</summary>
    public class ResourceSnapshot
    {
        public string ThingDef { get; set; }

        /// <summary>"Food", "RawResource", "Manufactured", "Medicine", or "Other".</summary>
        public string Category { get; set; }

        public int Count { get; set; }
        public bool IsPerishable { get; set; }
        public float AverageHitPoints { get; set; }
    }

    /// <summary>Current map terrain, rooms, and hazards.</summary>
    public class MapSnapshot
    {
        public string BiomeDef { get; set; }

        /// <summary>Short hash of terrain grid for diff detection.</summary>
        public string TerrainHash { get; set; }

        public int MapSizeX { get; set; }
        public int MapSizeZ { get; set; }
        public int RoofCollapseRisks { get; set; }
        public int FireCount { get; set; }
        public int FilthScore { get; set; }

        /// <summary>Surplus power in connected grid (watts).</summary>
        public int PowerNetExcess { get; set; }

        /// <summary>Top 5 rooms by wealth desc.</summary>
        public List<RoomSnapshot> TopRooms { get; set; }
    }

    /// <summary>A single room's stats.</summary>
    public class RoomSnapshot
    {
        public string Label { get; set; }
        public int CellCount { get; set; }
        public float Impressiveness { get; set; }
        public float Cleanliness { get; set; }
        public float Temperature { get; set; }
    }

    /// <summary>Immediate dangers to the colony.</summary>
    public class ThreatSnapshot
    {
        public int HostilePawnsOnMap { get; set; }
        public int DownedColonists { get; set; }

        /// <summary>Fires within 30 cells of any colony building.</summary>
        public int FiresNearBase { get; set; }

        /// <summary>Walls adjacent to exterior being attacked.</summary>
        public int BreachWallCandidates { get; set; }

        /// <summary>Distance to nearest hostile pawn; float.MaxValue if none.</summary>
        public float NearestHostileDistance { get; set; }

        public List<string> ActiveRaids { get; set; }
        public bool AnyMentalBreakRisk { get; set; }
    }

    /// <summary>Current research progress.</summary>
    public class ResearchSnapshot
    {
        public string CurrentProject { get; set; }
        public float CurrentProgress { get; set; }

        /// <summary>Last 20 completed research defNames.</summary>
        public List<string> CompletedTechs { get; set; }

        /// <summary>Next 10 unlockable research defNames.</summary>
        public List<string> AvailableTechs { get; set; }
    }

    /// <summary>Current weather conditions.</summary>
    public class WeatherSnapshot
    {
        public string WeatherDef { get; set; }
        public float WindSpeed { get; set; }
        public float OutdoorTemp { get; set; }
        public float IndoorTemp { get; set; }
        public float ToxicFallout { get; set; }
    }

    // =========================================================================
    // G-02 Additions: Buildings, Zones, Bills, Items, Policies, Factions
    // =========================================================================

    /// <summary>
    /// A building on the map. EntityId uses Thing.GetUniqueLoadID() per G-03.
    /// </summary>
    public class BuildingSnapshot
    {
        /// <summary>Save-stable entity ID per G-03: building.GetUniqueLoadID().</summary>
        public string EntityId { get; set; }

        /// <summary>Human-readable label.</summary>
        public string Label { get; set; }

        public string DefName { get; set; }

        /// <summary>"Production", "Comms", "Power", "Bed", "Storage", "Other".</summary>
        public string Kind { get; set; }

        public int X { get; set; }
        public int Z { get; set; }

        /// <summary>Whether the building is usable (powered, not broken, accessible).</summary>
        public bool Usable { get; set; }
    }

    /// <summary>
    /// A zone (growing, stockpile, dumping). EntityId = "zone_" + Zone.ID per G-03.
    /// </summary>
    public class ZoneSnapshot
    {
        /// <summary>Save-stable entity ID per G-03: "zone_" + Zone.ID.</summary>
        public string EntityId { get; set; }

        /// <summary>Human-readable label.</summary>
        public string Label { get; set; }

        /// <summary>"Growing", "Stockpile", "Dumping", "Other".</summary>
        public string ZoneType { get; set; }

        public int CellCount { get; set; }

        /// <summary>Plant defName for growing zones; null otherwise.</summary>
        public string PlantDefName { get; set; }
    }

    /// <summary>
    /// An active bill on a production table. EntityId = "bill_" + Bill.loadID per G-03.
    /// </summary>
    public class BillSnapshot
    {
        /// <summary>Save-stable entity ID per G-03: "bill_" + Bill.loadID.</summary>
        public string EntityId { get; set; }

        /// <summary>Human-readable label (recipe name).</summary>
        public string Label { get; set; }

        /// <summary>EntityId of the production table this bill belongs to.</summary>
        public string TableId { get; set; }

        public string RecipeDefName { get; set; }

        /// <summary>"MakeXTimes", "RepeatForever", "PauseOnSatisfaction".</summary>
        public string RepeatMode { get; set; }

        public int TargetCount { get; set; }
        public bool Suspended { get; set; }
    }

    /// <summary>
    /// A notable item on the map (weapon, apparel, medicine).
    /// EntityId uses Thing.GetUniqueLoadID() per G-03.
    /// </summary>
    public class ItemSnapshot
    {
        /// <summary>Save-stable entity ID per G-03: item.GetUniqueLoadID().</summary>
        public string EntityId { get; set; }

        /// <summary>Human-readable label.</summary>
        public string Label { get; set; }

        public string DefName { get; set; }

        /// <summary>"Awful" through "Legendary", or null for non-quality items.</summary>
        public string Quality { get; set; }

        public int StackCount { get; set; }
        public int X { get; set; }
        public int Z { get; set; }
    }

    /// <summary>
    /// Available outfit, drug policy, and food restriction names.
    /// The names themselves serve as IDs (no separate EntityId needed).
    /// </summary>
    public class PolicySnapshot
    {
        public List<string> OutfitNames { get; set; }
        public List<string> DrugPolicyNames { get; set; }
        public List<string> FoodRestrictionNames { get; set; }
    }

    /// <summary>
    /// A known faction. EntityId = "faction_" + Faction.loadID per G-03.
    /// </summary>
    public class FactionSnapshot
    {
        /// <summary>Save-stable entity ID per G-03: "faction_" + Faction.loadID.</summary>
        public string EntityId { get; set; }

        /// <summary>Human-readable faction name.</summary>
        public string Name { get; set; }

        public int Goodwill { get; set; }
        public bool Hostile { get; set; }
        public bool CanTradeNow { get; set; }
    }

    // =========================================================================
    // Perception Events — used by EventBus, CadenceController, Harmony hooks
    // =========================================================================

    /// <summary>Event categories surfaced by the perception layer via Harmony hooks.</summary>
    public enum PerceptionEventType
    {
        Letter,
        Alert,
        RaidDetected,
        PawnDied,
        PawnDowned,
        FireStarted,
        MentalBreak,
        MedicalEmergency,
        ManhunterPack,
        SiegeDetected,
        Infestation,
        SnapshotDelta,
        BudgetExceeded,
        AutonomyChanged,
        Error
    }

    /// <summary>
    /// Severity classification for events.
    /// High (3) forces immediate snapshot regardless of throttle.
    /// </summary>
    public enum CriticalLevel
    {
        None = 0,
        Low = 1,
        Medium = 2,
        High = 3
    }

    /// <summary>Event DTO published through EventBus.</summary>
    public class PerceptionEvent
    {
        public PerceptionEventType Type { get; set; }
        public CriticalLevel Criticality { get; set; }
        public int Tick { get; set; }
        public float GameHour { get; set; }

        /// <summary>EntityId of source pawn; null for map-level events.</summary>
        public string SourcePawnId { get; set; }

        /// <summary>Map the event originated from (G-24 multi-map tagging); -1 when not map-scoped.</summary>
        public int MapId { get; set; } = -1;

        public string Message { get; set; }

        /// <summary>Type-specific metadata (faction, severity, location, etc.).</summary>
        public Dictionary<string, string> Metadata { get; set; }

        /// <summary>Factory method for test convenience.</summary>
        public static PerceptionEvent Create(
            PerceptionEventType type,
            CriticalLevel criticality = CriticalLevel.None,
            int tick = 0,
            float gameHour = 0f,
            string sourcePawnId = null,
            string message = null,
            int mapId = -1)
        {
            return new PerceptionEvent
            {
                Type = type,
                Criticality = criticality,
                Tick = tick,
                GameHour = gameHour,
                SourcePawnId = sourcePawnId,
                Message = message,
                MapId = mapId,
                Metadata = new Dictionary<string, string>()
            };
        }
    }

    // =========================================================================
    // Snapshot Diff — produced by SnapshotDiffEngine, consumed by EventBus
    // =========================================================================

    /// <summary>
    /// Delta between two snapshots. Computed every snapshot cycle.
    /// Forwarded to EventBus as SnapshotDelta event type.
    /// </summary>
    public class SnapshotDiff
    {
        public int FromTick { get; set; }
        public int ToTick { get; set; }

        /// <summary>Pawn EntityIds present in current but not previous.</summary>
        public List<string> AddedPawns { get; set; }

        /// <summary>Pawn EntityIds present in previous but not current.</summary>
        public List<string> RemovedPawns { get; set; }

        /// <summary>Pawns that exist in both but have field changes.</summary>
        public List<PawnDelta> ChangedPawns { get; set; }

        /// <summary>Resource count changes by ThingDef.</summary>
        public List<ResourceDelta> ResourceChanges { get; set; }

        public bool ThreatLevelChanged { get; set; }

        /// <summary>1–2 sentence English summary from SnapshotDiffEngine.Summarize.</summary>
        public string NarrativeSummary { get; set; }
    }

    /// <summary>Changed fields for a single pawn between snapshots.</summary>
    public class PawnDelta
    {
        /// <summary>Pawn EntityId.</summary>
        public string PawnId { get; set; }

        /// <summary>e.g. ["Needs.Mood", "Health.BloodLoss", "Position"].</summary>
        public List<string> ChangedFields { get; set; }
    }

    /// <summary>Resource count change between snapshots.</summary>
    public class ResourceDelta
    {
        public string ThingDef { get; set; }
        public int OldCount { get; set; }
        public int NewCount { get; set; }
    }
}
