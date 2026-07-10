using System;
using System.Collections.Generic;
using System.Linq;
using RimWorld;
using Verse;

namespace RimAI.Agent.Perception
{
    /// <summary>
    /// Reads RimWorld game state on the main thread and produces a ColonySnapshot DTO.
    /// All Verse/RimWorld types stay inside this class; callers see only DTOs.
    ///
    /// Implements IPerceptionEngine per the layer spec.
    /// Uses Find.AnyPlayerHomeMap per G-24 (never Find.CurrentMap).
    ///
    /// Compile-only for M1 — full runtime population in later milestones.
    /// Uses only well-established RimWorld APIs defensively.
    /// </summary>
    public class SnapshotBuilder : IPerceptionEngine
    {
        private const int MaxColonists = 8;
        private const int MaxHostiles = 5;
        private const int MaxAnimals = 5;
        private const int MaxResources = 30;
        private const int MaxRooms = 5;
        private const int MaxBuildings = 20;
        private const int MaxBills = 20;
        private const int MaxNotableItems = 15;
        private const int MaxFactions = 8;

        public ColonySnapshot TakeSnapshot(ColonySnapshot prev)
        {
            var map = Find.AnyPlayerHomeMap;
            if (map == null) return EmptySnapshot();

            return new ColonySnapshot
            {
                Version = 2,
                Tick = Find.TickManager.TicksAbs,
                GameDay = GenDate.DayOfQuadrum(Find.TickManager.TicksAbs, 0f),
                TimeOfDay = GenLocalDate.HourFloat(map),
                Season = GenDate.Quadrum(Find.TickManager.TicksAbs, 0f).ToString(),
                Summary = BuildSummary(map),
                Pawns = BuildPawns(map),
                Resources = BuildResources(map),
                Map = BuildMap(map),
                Threats = BuildThreats(map),
                Research = BuildResearch(),
                Weather = BuildWeather(map),
                Buildings = BuildBuildings(map),
                Zones = BuildZones(map),
                Bills = BuildBills(map),
                NotableItems = BuildNotableItems(map),
                Policies = BuildPolicies(),
                Factions = BuildFactions()
            };
        }

        public SnapshotDiff DiffSnapshots(ColonySnapshot prev, ColonySnapshot current)
        {
            return SnapshotDiffEngine.Compute(prev, current);
        }

        private static ColonySnapshot EmptySnapshot()
        {
            return new ColonySnapshot
            {
                Version = 2, Tick = 0,
                Pawns = new List<PawnSnapshot>(),
                Resources = new List<ResourceSnapshot>(),
                Buildings = new List<BuildingSnapshot>(),
                Zones = new List<ZoneSnapshot>(),
                Bills = new List<BillSnapshot>(),
                NotableItems = new List<ItemSnapshot>(),
                Factions = new List<FactionSnapshot>()
            };
        }

        // ── Summary ──────────────────────────────────────────────────────────

        private static ColonySummary BuildSummary(Map map)
        {
            var freeColonists = map.mapPawns.FreeColonistsSpawned;
            return new ColonySummary
            {
                ColonistCount = map.mapPawns.FreeColonistsCount,
                PrisonerCount = map.mapPawns.PrisonersOfColonyCount,
                GuestCount = map.mapPawns.AllPawnsSpawned
                    .Count(p => !p.IsColonist && !p.IsPrisoner && p.RaceProps.Humanlike),
                AnimalCount = map.mapPawns.AllPawnsSpawned.Count(p => p.RaceProps.Animal),
                HostileCount = map.mapPawns.AllPawnsSpawned
                    .Count(p => p.Faction != null && p.Faction.HostileTo(Faction.OfPlayer)),
                ColonyWealth = 0f,
                TotalWealth = 0f,
                RaidPoints = StorytellerUtility.DefaultThreatPointsNow(map),
                ActiveIncidents = new List<string>(),
                AvgMood = freeColonists.Any()
                    ? freeColonists.Average(c => c.needs?.mood?.CurLevel ?? 0.5f) : 0f,
                AvgFoodNeed = freeColonists.Any()
                    ? freeColonists.Average(c => c.needs?.food?.CurLevel ?? 0.5f) : 0f
            };
        }

        // ── Pawns ────────────────────────────────────────────────────────────

        private static List<PawnSnapshot> BuildPawns(Map map)
        {
            try
            {
                var all = map.mapPawns.AllPawnsSpawned.ToList();
                var colonists = all.Where(p => p.IsColonist && !p.Dead).Take(MaxColonists).ToList();
                var colonistSet = new HashSet<Pawn>(colonists);
                var hostiles = all
                    .Where(p => !p.IsColonist && p.Faction != null
                        && p.Faction.HostileTo(Faction.OfPlayer) && !p.Dead)
                    .Where(p => !colonistSet.Contains(p))
                    .Take(MaxHostiles).ToList();
                var used = new HashSet<Pawn>(colonists.Concat(hostiles));
                var animals = all
                    .Where(p => p.RaceProps.Animal && !p.Dead && !used.Contains(p))
                    .Take(MaxAnimals).ToList();

                var result = new List<PawnSnapshot>();
                result.AddRange(colonists.Select(p => BuildPawnSnapshot(p, map)));
                result.AddRange(hostiles.Select(p => BuildPawnSnapshot(p, map)));
                result.AddRange(animals.Select(p => BuildPawnSnapshot(p, map)));
                return result;
            }
            catch { return new List<PawnSnapshot>(); }
        }

        private static PawnSnapshot BuildPawnSnapshot(Pawn pawn, Map map)
        {
            return new PawnSnapshot
            {
                EntityId = MintThingId(pawn),
                Label = pawn.LabelShort,
                KindDef = pawn.kindDef?.defName,
                FactionName = pawn.Faction?.Name,
                IsColonist = pawn.IsColonist,
                IsPrisoner = pawn.IsPrisoner,
                IsDowned = pawn.Downed,
                IsDrafted = pawn.Drafted,
                Needs = pawn.RaceProps.Humanlike ? BuildNeeds(pawn) : null,
                Skills = pawn.IsColonist ? BuildSkills(pawn) : null,
                Health = pawn.Dead ? null : BuildHealth(pawn),
                Work = pawn.IsColonist ? BuildWork(pawn) : null,
                Equipment = BuildEquipment(pawn),
                Position = BuildPosition(pawn, map)
            };
        }

        private static NeedsSnapshot BuildNeeds(Pawn pawn)
        {
            return new NeedsSnapshot
            {
                Mood = pawn.needs?.mood?.CurLevel ?? 0f,
                Food = pawn.needs?.food?.CurLevel ?? 0f,
                Rest = pawn.needs?.rest?.CurLevel ?? 0f,
                Recreation = pawn.needs?.joy?.CurLevel ?? 0f,
                Comfort = pawn.needs?.comfort?.CurLevel ?? 0f,
                Beauty = pawn.needs?.beauty?.CurLevel ?? 0f,
                Outdoors = pawn.needs?.outdoors?.CurLevel ?? 0f,
                TopThoughts = BuildThoughts(pawn)
            };
        }

        private static List<ThoughtSnapshot> BuildThoughts(Pawn pawn)
        {
            try
            {
                var memories = pawn.needs?.mood?.thoughts?.memories?.Memories;
                if (memories == null) return new List<ThoughtSnapshot>();
                return memories
                    .OrderByDescending(t => Math.Abs(t.MoodOffset()))
                    .Take(5)
                    .Select(t => new ThoughtSnapshot
                    {
                        Label = t.LabelCap,
                        MoodOffset = t.MoodOffset(),
                        ExpiresInTicks = t.DurationTicks
                    }).ToList();
            }
            catch { return new List<ThoughtSnapshot>(); }
        }

        private static SkillsSnapshot BuildSkills(Pawn pawn)
        {
            try
            {
                var skillDefs = pawn.skills?.skills;
                if (skillDefs == null) return new SkillsSnapshot();
                return new SkillsSnapshot
                {
                    Skills = skillDefs
                        .OrderByDescending(s => s.Level)
                        .ThenByDescending(s => (int)s.passion)
                        .Take(5)
                        .Select(s => new SkillSnapshot
                        {
                            DefName = s.def?.defName,
                            Passion = s.passion.ToString(),
                            Level = s.Level,
                            XpSinceLastLevel = GetSkillXp(s)
                        }).ToList()
                };
            }
            catch { return new SkillsSnapshot(); }
        }

        private static HealthSnapshot BuildHealth(Pawn pawn)
        {
            var hediffs = pawn.health?.hediffSet?.hediffs;
            return new HealthSnapshot
            {
                Consciousness = pawn.health?.capacities?.GetLevel(PawnCapacityDefOf.Consciousness) ?? 1f,
                Moving = pawn.health?.capacities?.GetLevel(PawnCapacityDefOf.Moving) ?? 1f,
                Manipulation = pawn.health?.capacities?.GetLevel(PawnCapacityDefOf.Manipulation) ?? 1f,
                Talking = pawn.health?.capacities?.GetLevel(PawnCapacityDefOf.Talking) ?? 1f,
                BloodLoss = pawn.health?.hediffSet?.BleedRateTotal ?? 0f,
                Pain = pawn.health?.hediffSet?.PainTotal ?? 0f,
                OpenWoundCount = hediffs?.Count(h => h.Bleeding) ?? 0,
                MissingPartCount = hediffs?.Count(h => h is Hediff_MissingPart) ?? 0,
                TopInjuries = BuildInjuries(pawn)
            };
        }

        private static List<InjurySnapshot> BuildInjuries(Pawn pawn)
        {
            try
            {
                var hediffs = pawn.health?.hediffSet?.hediffs;
                if (hediffs == null) return new List<InjurySnapshot>();
                return hediffs
                    .Where(h => h is Hediff_Injury || h is Hediff_MissingPart)
                    .OrderByDescending(h => h.Severity)
                    .Take(5)
                    .Select(h => new InjurySnapshot
                    {
                        Label = h.LabelCap,
                        BodyPartLabel = h.Part?.LabelCap,
                        Severity = h.Severity,
                        Bleeding = h.Bleeding,
                        Infected = false
                    }).ToList();
            }
            catch { return new List<InjurySnapshot>(); }
        }

        private static WorkSnapshot BuildWork(Pawn pawn)
        {
            var priorities = new Dictionary<string, int>();
            try
            {
                if (pawn.workSettings != null)
                {
                    foreach (var wtd in DefDatabase<WorkTypeDef>.AllDefs)
                    {
                        int p = pawn.workSettings.GetPriority(wtd);
                        if (p > 0) priorities[wtd.defName] = p;
                    }
                }
            }
            catch { }

            return new WorkSnapshot
            {
                CurrentJobDef = pawn.CurJob?.def?.defName,
                CurrentTargetLabel = pawn.CurJob?.targetA.Thing?.LabelCap,
                Priorities = priorities
            };
        }

        private static EquipSnapshot BuildEquipment(Pawn pawn)
        {
            try
            {
                var worn = pawn.apparel?.WornApparel;
                var labels = new List<string>();
                if (worn != null)
                {
                    var reversed = new List<Apparel>(worn);
                    reversed.Reverse();
                    labels = reversed.Take(5).Select(a => a.LabelCap).ToList();
                }

                return new EquipSnapshot
                {
                    WeaponLabel = pawn.equipment?.Primary?.LabelCap,
                    WeaponAvgDps = 0f,
                    ApparelLabels = labels,
                    ArmorBlunt = pawn.GetStatValue(StatDefOf.ArmorRating_Blunt),
                    ArmorSharp = pawn.GetStatValue(StatDefOf.ArmorRating_Sharp)
                };
            }
            catch { return new EquipSnapshot(); }
        }

        private static PositionSnapshot BuildPosition(Pawn pawn, Map map)
        {
            try
            {
                var room = pawn.GetRoom(RegionType.Set_All);
                return new PositionSnapshot
                {
                    X = pawn.Position.x,
                    Z = pawn.Position.z,
                    RoomLabel = room?.GetRoomRoleLabel()
                };
            }
            catch { return new PositionSnapshot { X = pawn.Position.x, Z = pawn.Position.z }; }
        }

        // ── Resources ────────────────────────────────────────────────────────

        private static List<ResourceSnapshot> BuildResources(Map map)
        {
            try
            {
                var haulables = map.listerThings?.ThingsInGroup(ThingRequestGroup.HaulableEver);
                if (haulables == null) return new List<ResourceSnapshot>();

                return haulables
                    .Where(t => t.def?.category != ThingCategory.Pawn)
                    .GroupBy(t => t.def.defName)
                    .Select(g =>
                    {
                        var def = g.First().def;
                        return new ResourceSnapshot
                        {
                            ThingDef = g.Key,
                            Category = ClassifyCategory(def),
                            Count = g.Sum(t => t.stackCount),
                            IsPerishable = def.HasComp(typeof(CompRottable)),
                            AverageHitPoints = g.Any() ? (float)g.Average(t => t.HitPoints) : 0f
                        };
                    })
                    .OrderByDescending(r => r.Count)
                    .Take(MaxResources)
                    .ToList();
            }
            catch { return new List<ResourceSnapshot>(); }
        }

        private static string ClassifyCategory(ThingDef def)
        {
            if (def == null) return "Other";
            if (def.ingestible != null) return "Food";
            if (def.IsMedicine || def.IsDrug) return "Medicine";
            if (def.IsRangedWeapon || def.IsMeleeWeapon || def.IsApparel) return "Manufactured";
            if (def.stuffProps != null) return "RawResource";
            return "Other";
        }

        // ── Map ──────────────────────────────────────────────────────────────

        private static MapSnapshot BuildMap(Map map)
        {
            return new MapSnapshot
            {
                BiomeDef = map.Biome?.defName,
                TerrainHash = ComputeHash(map),
                MapSizeX = map.Size.x,
                MapSizeZ = map.Size.z,
                RoofCollapseRisks = 0,
                FireCount = 0,
                FilthScore = 0,
                PowerNetExcess = 0,
                TopRooms = BuildRooms(map)
            };
        }

        private static string ComputeHash(Map map)
        {
            try
            {
                int hash = 17;
                var grid = map.terrainGrid;
                if (grid == null) return "00000000";
                for (int z = 0; z < map.Size.z; z += 10)
                {
                    for (int x = 0; x < map.Size.x; x += 10)
                    {
                        var terrain = grid.TerrainAt(new IntVec3(x, 0, z));
                        if (terrain != null)
                            hash = hash * 31 + terrain.defName.GetHashCode();
                    }
                }
                return hash.ToString("X8");
            }
            catch { return "00000000"; }
        }

        private static List<RoomSnapshot> BuildRooms(Map map)
        {
            try
            {
                var rooms = GetMapRooms(map);
                if (rooms == null) return new List<RoomSnapshot>();

                return rooms
                    .Where(r => r.Role != RoomRoleDefOf.None)
                    .OrderByDescending(r =>
                    {
                        try { return r.GetStat(RoomStatDefOf.Wealth); }
                        catch { return 0f; }
                    })
                    .Take(MaxRooms)
                    .Select(r => new RoomSnapshot
                    {
                        Label = r.Role?.label ?? "Room",
                        CellCount = r.CellCount,
                        Impressiveness = TryStat(r, RoomStatDefOf.Impressiveness),
                        Cleanliness = TryStat(r, RoomStatDefOf.Cleanliness),
                        Temperature = r.Temperature
                    }).ToList();
            }
            catch { return new List<RoomSnapshot>(); }
        }

        /// <summary>
        /// Access the allRooms list from RegionGrid via reflection.
        /// The field name/type may vary between RimWorld versions.
        /// </summary>
        private static List<Room> GetMapRooms(Map map)
        {
            try
            {
                var grid = map.regionGrid;
                if (grid == null) return null;
                var field = typeof(RegionGrid).GetField("allRooms",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                return field?.GetValue(grid) as List<Room>;
            }
            catch { return null; }
        }

        private static float TryStat(Room room, RoomStatDef stat)
        {
            try { return room.GetStat(stat); }
            catch { return 0f; }
        }

        // ── Threats ──────────────────────────────────────────────────────────

        private static ThreatSnapshot BuildThreats(Map map)
        {
            try
            {
                var hostiles = map.mapPawns?.AllPawnsSpawned?
                    .Where(p => p.Faction != null && p.Faction.HostileTo(Faction.OfPlayer))
                    .ToList() ?? new List<Pawn>();

                float nearest = float.MaxValue;
                if (hostiles.Count > 0)
                {
                    nearest = hostiles.Min(h => h.Position.DistanceTo(map.Center));
                }

                return new ThreatSnapshot
                {
                    HostilePawnsOnMap = hostiles.Count,
                    DownedColonists = map.mapPawns?.FreeColonistsSpawned?.Count(c => c.Downed) ?? 0,
                    FiresNearBase = 0,
                    BreachWallCandidates = 0,
                    NearestHostileDistance = nearest,
                    ActiveRaids = new List<string>(),
                    AnyMentalBreakRisk = map.mapPawns?.FreeColonistsSpawned?
                        .Any(c => (c.needs?.mood?.CurLevel ?? 1f) < 0.25f) ?? false
                };
            }
            catch { return new ThreatSnapshot(); }
        }

        // ── Research ─────────────────────────────────────────────────────────

        private static ResearchSnapshot BuildResearch()
        {
            try
            {
                var rm = Find.ResearchManager;
                return new ResearchSnapshot
                {
                    CurrentProject = rm?.GetProject()?.defName,
                    CurrentProgress = 0f,
                    CompletedTechs = new List<string>(),
                    AvailableTechs = new List<string>()
                };
            }
            catch { return new ResearchSnapshot(); }
        }

        // ── Weather ──────────────────────────────────────────────────────────

        private static WeatherSnapshot BuildWeather(Map map)
        {
            try
            {
                return new WeatherSnapshot
                {
                    WeatherDef = map.weatherManager?.curWeather?.defName,
                    WindSpeed = map.windManager?.WindSpeed ?? 0f,
                    OutdoorTemp = map.mapTemperature?.OutdoorTemp ?? 0f,
                    IndoorTemp = 21f,
                    ToxicFallout = 0f
                };
            }
            catch { return new WeatherSnapshot(); }
        }

        // ── G-02: Buildings ──────────────────────────────────────────────────

        private static List<BuildingSnapshot> BuildBuildings(Map map)
        {
            try
            {
                var buildings = map.listerBuildings?.allBuildingsColonist;
                if (buildings == null) return new List<BuildingSnapshot>();

                return buildings
                    .OrderByDescending(b =>
                    {
                        if (b?.def == null) return 0;
                        if (b.def.IsWorkTable) return 2;
                        if (b.def.IsBed) return 1;
                        return 0;
                    })
                    .Take(MaxBuildings)
                    .Select(b => new BuildingSnapshot
                    {
                        EntityId = MintThingId(b),
                        Label = b.LabelCap,
                        DefName = b.def?.defName,
                        Kind = KindOf(b),
                        X = b.Position.x,
                        Z = b.Position.z,
                        Usable = IsUsable(b)
                    }).ToList();
            }
            catch { return new List<BuildingSnapshot>(); }
        }

        private static string KindOf(Building b)
        {
            if (b?.def == null) return "Other";
            if (b.def.IsWorkTable) return "Production";
            if (b.def.IsBed) return "Bed";
            return "Other";
        }

        private static bool IsUsable(Building b)
        {
            if (b == null) return false;
            try
            {
                var cp = b.GetComp<CompPowerTrader>();
                if (cp != null && !cp.PowerOn) return false;
                var cb = b.GetComp<CompBreakdownable>();
                if (cb != null && cb.BrokenDown) return false;
                return true;
            }
            catch { return true; }
        }

        // ── G-02: Zones ─────────────────────────────────────────────────────

        private static List<ZoneSnapshot> BuildZones(Map map)
        {
            try
            {
                var zones = map.zoneManager?.AllZones;
                if (zones == null) return new List<ZoneSnapshot>();

                return zones.Select(z => new ZoneSnapshot
                {
                    EntityId = MintZoneId(z),
                    Label = z.label,
                    ZoneType = z is Zone_Growing ? "Growing"
                        : z is Zone_Stockpile ? "Stockpile" : "Other",
                    CellCount = z.Cells?.Count() ?? 0,
                    PlantDefName = PlantDefOf(z)
                }).ToList();
            }
            catch { return new List<ZoneSnapshot>(); }
        }

        private static string PlantDefOf(Zone z)
        {
            try
            {
                var gz = z as Zone_Growing;
                // plantDefToGrow may be a field or property depending on RW version
                if (gz == null) return null;
                try
                {
                    var plantField = typeof(Zone_Growing).GetProperty("plantDefToGrow",
                        System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                    if (plantField != null)
                        return (plantField.GetValue(gz) as ThingDef)?.defName;
                    var plantFld = typeof(Zone_Growing).GetField("plantDefToGrow",
                        System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                    if (plantFld != null)
                        return (plantFld.GetValue(gz) as ThingDef)?.defName;
                }
                catch { }
                return null;
            }
            catch { return null; }
        }

        // ── G-02: Bills ──────────────────────────────────────────────────────

        private static List<BillSnapshot> BuildBills(Map map)
        {
            try
            {
                var workTables = map.listerBuildings?.allBuildingsColonist?
                    .Where(b => b.def?.IsWorkTable == true)
                    ?? Enumerable.Empty<Building>();

                var allBills = new List<(Building table, Bill bill)>();
                foreach (var table in workTables)
                {
                    try
                    {
                        var giver = table as IBillGiver;
                        var stack = giver?.BillStack;
                        if (stack == null) continue;
                        foreach (var bill in stack.Bills)
                            allBills.Add((table, bill));
                    }
                    catch { }
                }

                return allBills.Take(MaxBills).Select(x => new BillSnapshot
                {
                    EntityId = MintBillId(x.bill),
                    Label = x.bill.LabelCap,
                    TableId = MintThingId(x.table),
                    RecipeDefName = x.bill.recipe?.defName,
                    RepeatMode = "RepeatForever",
                    TargetCount = 0,
                    Suspended = x.bill.suspended
                }).ToList();
            }
            catch { return new List<BillSnapshot>(); }
        }

        // ── G-02: NotableItems ───────────────────────────────────────────────

        private static List<ItemSnapshot> BuildNotableItems(Map map)
        {
            try
            {
                var weapons = map.listerThings?.ThingsInGroup(ThingRequestGroup.Weapon)
                    ?.Cast<Thing>() ?? Enumerable.Empty<Thing>();
                var apparel = map.listerThings?.ThingsInGroup(ThingRequestGroup.Apparel)
                    ?.Cast<Thing>() ?? Enumerable.Empty<Thing>();
                var medicine = map.listerThings?.ThingsInGroup(ThingRequestGroup.Medicine)
                    ?.Cast<Thing>() ?? Enumerable.Empty<Thing>();

                return weapons.Concat(apparel).Concat(medicine)
                    .Where(t => t.def?.category != ThingCategory.Pawn)
                    .OrderByDescending(t =>
                    {
                        QualityCategory qc;
                        return t.TryGetQuality(out qc) ? (int)qc : 0;
                    })
                    .Take(MaxNotableItems)
                    .Select(t =>
                    {
                        QualityCategory qc;
                        bool has = t.TryGetQuality(out qc);
                        return new ItemSnapshot
                        {
                            EntityId = MintThingId(t),
                            Label = t.LabelCap,
                            DefName = t.def?.defName,
                            Quality = has ? qc.GetLabel() : null,
                            StackCount = t.stackCount,
                            X = t.Position.x,
                            Z = t.Position.z
                        };
                    }).ToList();
            }
            catch { return new List<ItemSnapshot>(); }
        }

        // ── G-02: Policies ───────────────────────────────────────────────────

        private static PolicySnapshot BuildPolicies()
        {
            try
            {
                var game = Current.Game;
                return new PolicySnapshot
                {
                    OutfitNames = game?.outfitDatabase?.AllOutfits?
                        .Select(o => o.label).ToList() ?? new List<string>(),
                    DrugPolicyNames = game?.drugPolicyDatabase?.AllPolicies?
                        .Select(p => p.label).ToList() ?? new List<string>(),
                    FoodRestrictionNames = game?.foodRestrictionDatabase?.AllFoodRestrictions?
                        .Select(f => f.label).ToList() ?? new List<string>()
                };
            }
            catch { return new PolicySnapshot(); }
        }

        // ── G-02: Factions ───────────────────────────────────────────────────

        private static List<FactionSnapshot> BuildFactions()
        {
            try
            {
                var factions = Find.FactionManager?.AllFactionsVisible;
                if (factions == null) return new List<FactionSnapshot>();

                return factions
                    .Where(f => f != Faction.OfPlayer)
                    .OrderBy(f => f.def?.defName)
                    .Take(MaxFactions)
                    .Select(f => new FactionSnapshot
                    {
                        EntityId = MintFactionId(f),
                        Name = f.Name,
                        Goodwill = f.PlayerGoodwill,
                        Hostile = f.HostileTo(Faction.OfPlayer),
                        CanTradeNow = !f.HostileTo(Faction.OfPlayer) && f.PlayerGoodwill >= 0
                    }).ToList();
            }
            catch { return new List<FactionSnapshot>(); }
        }

        // ── EntityId Minting (G-03) ──────────────────────────────────────────

        private static string MintThingId(Thing thing)
        {
            if (thing == null) return null;
            try { return thing.GetUniqueLoadID(); }
            catch { return null; }
        }

        private static string MintZoneId(Zone zone)
        {
            if (zone == null) return null;
            try { return "zone_" + zone.ID; }
            catch { return null; }
        }

        private static string MintBillId(Bill bill)
        {
            if (bill == null) return null;
            try { return EntityIdFormatter.FromLoadId("bill_", bill.GetUniqueLoadID()); }
            catch { return null; }
        }

        private static string MintFactionId(Faction faction)
        {
            if (faction == null) return null;
            try { return EntityIdFormatter.FromLoadId("faction_", faction.GetUniqueLoadID()); }
            catch { return null; }
        }

        /// <summary>
        /// Access SkillRecord.XpSinceLastLevel — property name varies by RW version.
        /// Falls back to 0f if the property is inaccessible.
        /// </summary>
        private static float GetSkillXp(SkillRecord skill)
        {
            if (skill == null) return 0f;
            try
            {
                var prop = typeof(SkillRecord).GetProperty("XpSinceLastLevel",
                    System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.Instance);
                if (prop != null) return (float)prop.GetValue(skill);
            }
            catch { }
            return 0f;
        }
    }
}
