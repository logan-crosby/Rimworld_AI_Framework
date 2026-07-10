using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;
using RimAI.Agent.Perception;
using Xunit;

namespace RimAI.Agent.Tests.Perception
{
    /// <summary>
    /// Unit tests for ColonySnapshotTrimmer — pure logic, testable on net8.0.
    /// </summary>
    public class SnapshotTrimmerTests
    {
        // ── EstimateTokens ──────────────────────────────────────────────────

        [Fact]
        public void EstimateTokens_EmptyString_ReturnsZero()
        {
            Assert.Equal(0, ColonySnapshotTrimmer.EstimateTokens(""));
            Assert.Equal(0, ColonySnapshotTrimmer.EstimateTokens(null));
        }

        [Fact]
        public void EstimateTokens_SingleToken_WorthOne()
        {
            // 4 chars = 1 token
            Assert.Equal(1, ColonySnapshotTrimmer.EstimateTokens("abcd"));
        }

        [Fact]
        public void EstimateTokens_FiveChars_RoundsUp()
        {
            // 5 chars / 4 = 1.25 → ceiling = 2
            Assert.Equal(2, ColonySnapshotTrimmer.EstimateTokens("abcde"));
        }

        [Fact]
        public void EstimateTokens_LargeJson_CorrectRoundedEstimate()
        {
            var json = new string('x', 1000);
            Assert.Equal(250, ColonySnapshotTrimmer.EstimateTokens(json)); // 1000 / 4
        }

        // ── Trim: null / empty ──────────────────────────────────────────────

        [Fact]
        public void Trim_NullSnapshot_ReturnsNull()
        {
            Assert.Null(ColonySnapshotTrimmer.Trim(null));
        }

        [Fact]
        public void Trim_EmptySnapshot_ReturnsUnchanged()
        {
            var snapshot = new ColonySnapshot();
            var result = ColonySnapshotTrimmer.Trim(snapshot, 100);
            Assert.NotNull(result);
            Assert.Equal(2, result.Version);
        }

        [Fact]
        public void Trim_UnderBudget_ReturnsUnchanged()
        {
            var snapshot = BuildSmallSnapshot();
            int estimatedTokens = EstimateSnapshot(snapshot);

            // Trim with a budget larger than current size
            var result = ColonySnapshotTrimmer.Trim(snapshot, estimatedTokens + 1000);
            Assert.NotNull(result);

            // Verify key fields are still present
            Assert.NotNull(result.Pawns);
            Assert.NotEmpty(result.Pawns);
            Assert.NotNull(result.Resources);
        }

        // ── Trim: priority level 1 — NotableItems ───────────────────────────

        [Fact]
        public void Trim_DropsNotableItems_WhenOverBudget()
        {
            var snapshot = BuildSnapshotWithAllFields(pawnCount: 3);
            // Force our snapshot to have many items
            snapshot.NotableItems = BuildItems(20);
            snapshot.Factions = BuildFactions(5);

            // Trim with very low budget
            var result = ColonySnapshotTrimmer.Trim(snapshot, 50);

            // NotableItems should be null (first to go)
            Assert.Null(result.NotableItems);
        }

        // ── Trim: priority level 2 — Factions ───────────────────────────────

        [Fact]
        public void Trim_DropsFactions_WhenOverBudget()
        {
            var snapshot = BuildSnapshotWithAllFields(pawnCount: 3);
            snapshot.NotableItems = null; // skip level 1
            snapshot.Factions = BuildFactions(5);

            var result = ColonySnapshotTrimmer.Trim(snapshot, 50);

            // Factions should be null (second to go)
            Assert.Null(result.Factions);
        }

        // ── Trim: priority level 3 — Buildings beyond 10 ────────────────────

        [Fact]
        public void Trim_ReducesBuildingsToTen()
        {
            var snapshot = BuildSnapshotWithAllFields(pawnCount: 3);
            snapshot.NotableItems = null;
            snapshot.Factions = null;
            snapshot.Buildings = BuildBuildings(25); // 25 buildings

            var result = ColonySnapshotTrimmer.Trim(snapshot, 500);

            Assert.NotNull(result.Buildings);
            Assert.True(result.Buildings.Count <= 10);
        }

        [Fact]
        public void Trim_BuildingsAlreadyUnderCap_NotReduced()
        {
            var snapshot = BuildSnapshotWithAllFields(pawnCount: 3);
            snapshot.NotableItems = null;
            snapshot.Factions = null;
            snapshot.Buildings = BuildBuildings(5); // 5 buildings

            var result = ColonySnapshotTrimmer.Trim(snapshot, 500);

            Assert.NotNull(result.Buildings);
            Assert.Equal(5, result.Buildings.Count);
        }

        // ── Trim: priority level 4 — Bills beyond 10 ────────────────────────

        [Fact]
        public void Trim_ReducesBillsToTen()
        {
            var snapshot = BuildSnapshotWithAllFields(pawnCount: 3);
            snapshot.NotableItems = null;
            snapshot.Factions = null;
            snapshot.Buildings = BuildBuildings(5);
            snapshot.Bills = BuildBills(25); // 25 bills

            var result = ColonySnapshotTrimmer.Trim(snapshot, 500);

            Assert.NotNull(result.Bills);
            Assert.True(result.Bills.Count <= 10);
        }

        // ── Trim: priority level 5 — Resources beyond 30 ────────────────────

        [Fact]
        public void Trim_ReducesResourcesToThirty()
        {
            var snapshot = BuildSnapshotWithAllFields(pawnCount: 3);
            snapshot.NotableItems = null;
            snapshot.Factions = null;
            snapshot.Buildings = BuildBuildings(5);
            snapshot.Bills = BuildBills(5);
            snapshot.Resources = BuildResources(50); // 50 resources

            var result = ColonySnapshotTrimmer.Trim(snapshot, 500);

            Assert.NotNull(result.Resources);
            Assert.True(result.Resources.Count <= 30);
        }

        // ── Trim: priority level 6 — TopRooms beyond 3 ──────────────────────

        [Fact]
        public void Trim_ReducesTopRoomsToThree()
        {
            var snapshot = BuildSnapshotWithAllFields(pawnCount: 3);
            snapshot.NotableItems = null;
            snapshot.Factions = null;
            snapshot.Buildings = BuildBuildings(5);
            snapshot.Bills = BuildBills(5);
            snapshot.Resources = BuildResources(10);
            snapshot.Map = new MapSnapshot { TopRooms = BuildRooms(10) };

            var result = ColonySnapshotTrimmer.Trim(snapshot, 500);

            Assert.NotNull(result.Map?.TopRooms);
            Assert.True(result.Map.TopRooms.Count <= 3);
        }

        // ── Trim: priority level 7 — Pawns beyond 8 ─────────────────────────

        [Fact]
        public void Trim_ReducesPawnsToEight_KeepsColonistsFirst()
        {
            var snapshot = BuildSnapshotWithAllFields(pawnCount: 3);
            snapshot.NotableItems = null;
            snapshot.Factions = null;
            snapshot.Buildings = null;
            snapshot.Bills = null;
            snapshot.Resources = null;
            snapshot.Map = null;

            // Add 15 pawns: 5 colonists, 5 hostiles, 5 animals
            var pawns = new List<PawnSnapshot>();
            for (int i = 0; i < 5; i++)
                pawns.Add(BuildPawn($"c{i}", $"Colonist{i}", isColonist: true));
            for (int i = 0; i < 5; i++)
                pawns.Add(BuildPawn($"h{i}", $"Hostile{i}", isColonist: false,
                    factionName: "Pirates"));
            for (int i = 0; i < 5; i++)
                pawns.Add(BuildPawn($"a{i}", $"Animal{i}", isColonist: false));
            snapshot.Pawns = pawns;

            var result = ColonySnapshotTrimmer.Trim(snapshot, 2000);

            Assert.NotNull(result.Pawns);
            Assert.True(result.Pawns.Count <= 8,
                $"Expected ≤8 pawns, got {result.Pawns.Count}");

            // First pawns should be colonists (highest priority)
            var keptIds = result.Pawns.Select(p => p.EntityId).ToList();
            Assert.Contains("c0", keptIds);
            Assert.Contains("c1", keptIds);
            Assert.Contains("c2", keptIds);
            Assert.Contains("c3", keptIds);
            Assert.Contains("c4", keptIds);
        }

        // ── Trim: priority level 8 — per-pawn WorkSnapshot ──────────────────

        [Fact]
        public void Trim_DropsWorkSnapshotPerPawn()
        {
            var snapshot = BuildSnapshotWithAllFields(pawnCount: 3);
            snapshot.NotableItems = null;
            snapshot.Factions = null;
            snapshot.Buildings = null;
            snapshot.Bills = null;
            snapshot.Resources = null;
            snapshot.Map = null;

            // All pawns have WorkSnapshot populated
            Assert.NotNull(snapshot.Pawns[0].Work);

            var result = ColonySnapshotTrimmer.Trim(snapshot, 100);

            // WorkSnapshot should be null on all pawns
            foreach (var pawn in result.Pawns)
            {
                Assert.Null(pawn.Work);
            }
        }

        // ── Trim: priority level 9 — per-pawn EquipSnapshot ─────────────────

        [Fact]
        public void Trim_DropsEquipSnapshotPerPawn()
        {
            var snapshot = BuildSnapshotWithAllFields(pawnCount: 3);
            snapshot.NotableItems = null;
            snapshot.Factions = null;
            snapshot.Buildings = null;
            snapshot.Bills = null;
            snapshot.Resources = null;
            snapshot.Map = null;
            // Already dropped WorkSnapshot in previous level
            foreach (var p in snapshot.Pawns) p.Work = null;

            Assert.NotNull(snapshot.Pawns[0].Equipment);

            var result = ColonySnapshotTrimmer.Trim(snapshot, 50);

            foreach (var pawn in result.Pawns)
            {
                Assert.Null(pawn.Equipment);
            }
        }

        // ── Trim: priority level 10 — thoughts beyond 3 ─────────────────────

        [Fact]
        public void Trim_ReducesThoughtsToThree()
        {
            var snapshot = BuildSnapshotWithAllFields(pawnCount: 2);
            snapshot.NotableItems = null;
            snapshot.Factions = null;
            snapshot.Buildings = null;
            snapshot.Bills = null;
            snapshot.Resources = null;
            snapshot.Map = null;
            foreach (var p in snapshot.Pawns)
            {
                p.Work = null;
                p.Equipment = null;
            }

            // Give pawns 10 thoughts each
            foreach (var pawn in snapshot.Pawns)
            {
                pawn.Needs = new NeedsSnapshot
                {
                    TopThoughts = Enumerable.Range(0, 10)
                        .Select(i => new ThoughtSnapshot
                        {
                            Label = $"Thought{i}",
                            MoodOffset = i - 5f
                        })
                        .ToList()
                };
            }

            var result = ColonySnapshotTrimmer.Trim(snapshot, 50);

            foreach (var pawn in result.Pawns)
            {
                if (pawn.Needs?.TopThoughts != null)
                    Assert.True(pawn.Needs.TopThoughts.Count <= 3,
                        $"Expected ≤3 thoughts, got {pawn.Needs.TopThoughts.Count}");
            }
        }

        // ── Trim: priority level 11 — skills beyond 3 ────────────────────────

        [Fact]
        public void Trim_ReducesSkillsToThree()
        {
            var snapshot = BuildSnapshotWithAllFields(pawnCount: 2);
            snapshot.NotableItems = null;
            snapshot.Factions = null;
            snapshot.Buildings = null;
            snapshot.Bills = null;
            snapshot.Resources = null;
            snapshot.Map = null;
            foreach (var p in snapshot.Pawns)
            {
                p.Work = null;
                p.Equipment = null;
                if (p.Needs != null) p.Needs.TopThoughts = null;
            }

            // Give pawns 10 skills each
            foreach (var pawn in snapshot.Pawns)
            {
                pawn.Skills = new SkillsSnapshot
                {
                    Skills = Enumerable.Range(0, 10)
                        .Select(i => new SkillSnapshot
                        {
                            DefName = $"Skill{i}",
                            Passion = "None",
                            Level = 10 - i
                        })
                        .ToList()
                };
            }

            var result = ColonySnapshotTrimmer.Trim(snapshot, 50);

            foreach (var pawn in result.Pawns)
            {
                if (pawn.Skills?.Skills != null)
                    Assert.True(pawn.Skills.Skills.Count <= 3,
                        $"Expected ≤3 skills, got {pawn.Skills.Skills.Count}");
            }
        }

        // ── Trim: does not infinite loop ────────────────────────────────────

        [Fact]
        public void Trim_MaximumTrim_DoesNotInfiniteLoop()
        {
            // Create an extremely large snapshot
            var snapshot = BuildSnapshotWithAllFields(pawnCount: 20);
            snapshot.Buildings = BuildBuildings(100);
            snapshot.Bills = BuildBills(100);
            snapshot.Resources = BuildResources(200);
            snapshot.NotableItems = BuildItems(100);
            snapshot.Factions = BuildFactions(50);
            if (snapshot.Map == null) snapshot.Map = new MapSnapshot();
            snapshot.Map.TopRooms = BuildRooms(50);

            // Trim with impossibly small budget
            var result = ColonySnapshotTrimmer.Trim(snapshot, 10);

            // Should not throw, should not infinite loop
            Assert.NotNull(result);
        }

        // ── TrimToBudget(string) ────────────────────────────────────────────

        [Fact]
        public void TrimToBudget_String_UnderBudget_ReturnsOriginal()
        {
            var snapshot = BuildSmallSnapshot();
            var json = Serialize(snapshot);

            var result = ColonySnapshotTrimmer.TrimToBudget(json, 10000);

            // Should return valid JSON
            Assert.NotNull(result);
            var deserialized = JsonConvert.DeserializeObject<ColonySnapshot>(result,
                ColonySnapshotTrimmer.SerializerSettings);
            Assert.NotNull(deserialized);
        }

        [Fact]
        public void TrimToBudget_String_Empty_ReturnsEmpty()
        {
            Assert.Equal("", ColonySnapshotTrimmer.TrimToBudget(""));
            Assert.Null(ColonySnapshotTrimmer.TrimToBudget(null));
        }

        // ── Test helpers ────────────────────────────────────────────────────

        /// <summary>Serialize snapshot using same settings as trimmer.</summary>
        private static string Serialize(ColonySnapshot snapshot)
        {
            return JsonConvert.SerializeObject(snapshot, ColonySnapshotTrimmer.SerializerSettings);
        }

        /// <summary>Estimate token count for a snapshot.</summary>
        private static int EstimateSnapshot(ColonySnapshot snapshot)
        {
            return ColonySnapshotTrimmer.EstimateTokens(Serialize(snapshot));
        }

        private static ColonySnapshot BuildSmallSnapshot()
        {
            return new ColonySnapshot
            {
                Version = 2,
                Tick = 10000,
                Season = "Summer",
                Pawns = new List<PawnSnapshot>
                {
                    BuildPawn("pawn1", "Alice", isColonist: true),
                    BuildPawn("pawn2", "Bob", isColonist: true)
                },
                Resources = BuildResources(5)
            };
        }

        /// <summary>Build a snapshot with all fields populated at the given scale.</summary>
        private static ColonySnapshot BuildSnapshotWithAllFields(int pawnCount = 3)
        {
            var pawns = new List<PawnSnapshot>();
            for (int i = 0; i < pawnCount; i++)
            {
                pawns.Add(BuildPawn(
                    entityId: $"pawn_{i}",
                    label: $"Colonist{i}",
                    isColonist: true,
                    withDetails: true));
            }

            return new ColonySnapshot
            {
                Version = 2,
                Tick = 50000,
                GameDay = 15,
                TimeOfDay = 12.5f,
                Season = "Summer",
                Summary = new ColonySummary
                {
                    ColonistCount = pawnCount,
                    PrisonerCount = 0,
                    GuestCount = 0,
                    AnimalCount = 1,
                    HostileCount = 0,
                    ColonyWealth = 50000f,
                    TotalWealth = 75000f,
                    RaidPoints = 120f,
                    ActiveIncidents = new List<string>(),
                    AvgMood = 0.75f,
                    AvgFoodNeed = 0.8f
                },
                Pawns = pawns,
                Resources = BuildResources(10),
                Map = new MapSnapshot
                {
                    BiomeDef = "TemperateForest",
                    MapSizeX = 250,
                    MapSizeZ = 250,
                    TopRooms = BuildRooms(3)
                },
                Threats = new ThreatSnapshot
                {
                    HostilePawnsOnMap = 0,
                    DownedColonists = 0,
                    FiresNearBase = 0,
                    BreachWallCandidates = 0,
                    NearestHostileDistance = float.MaxValue,
                    AnyMentalBreakRisk = false
                },
                Research = new ResearchSnapshot
                {
                    CurrentProject = "MicroelectronicsBasics",
                    CurrentProgress = 0.5f
                },
                Weather = new WeatherSnapshot
                {
                    WeatherDef = "Clear",
                    WindSpeed = 1.5f,
                    OutdoorTemp = 21f,
                    IndoorTemp = 21f
                }
            };
        }

        private static PawnSnapshot BuildPawn(
            string entityId,
            string label,
            bool isColonist = false,
            string factionName = null,
            bool withDetails = true)
        {
            return new PawnSnapshot
            {
                EntityId = entityId,
                Label = label,
                KindDef = "Colonist",
                FactionName = factionName,
                IsColonist = isColonist,
                IsPrisoner = false,
                IsDowned = false,
                IsDrafted = false,
                Needs = withDetails ? new NeedsSnapshot
                {
                    Mood = 0.7f,
                    Food = 0.8f,
                    Rest = 0.9f,
                    Recreation = 0.6f,
                    Comfort = 0.5f,
                    Beauty = 0.5f,
                    Outdoors = 0.3f,
                    TopThoughts = new List<ThoughtSnapshot>
                    {
                        new ThoughtSnapshot
                        {
                            Label = "Ate fine meal",
                            MoodOffset = 5f,
                            ExpiresInTicks = 3000
                        }
                    }
                } : null,
                Skills = withDetails ? new SkillsSnapshot
                {
                    Skills = new List<SkillSnapshot>
                    {
                        new SkillSnapshot
                        {
                            DefName = "Shooting",
                            Passion = "Minor",
                            Level = 8,
                            XpSinceLastLevel = 2000f
                        }
                    }
                } : null,
                Health = new HealthSnapshot
                {
                    Consciousness = 1f,
                    Moving = 1f,
                    Manipulation = 1f,
                    Talking = 1f,
                    BloodLoss = 0f,
                    Pain = 0f,
                    OpenWoundCount = 0,
                    MissingPartCount = 0
                },
                Work = withDetails ? new WorkSnapshot
                {
                    CurrentJobDef = "Wait_MaintainPosture",
                    Priorities = new Dictionary<string, int>
                    {
                        { "Firefighter", 1 },
                        { "Patient", 1 },
                        { "Doctor", 2 },
                        { "Cooking", 3 }
                    }
                } : null,
                Equipment = new EquipSnapshot
                {
                    WeaponLabel = "Revolver",
                    WeaponAvgDps = 3.5f,
                    ApparelLabels = new List<string> { "T-shirt", "Pants" },
                    ArmorBlunt = 0.2f,
                    ArmorSharp = 0.1f
                },
                Position = new PositionSnapshot
                {
                    X = 50,
                    Z = 50,
                    RoomLabel = "Barracks",
                    AreaDef = "Home"
                }
            };
        }

        private static List<ResourceSnapshot> BuildResources(int count)
        {
            return Enumerable.Range(0, count)
                .Select(i => new ResourceSnapshot
                {
                    ThingDef = $"Resource{i % 5}_{i}",
                    Category = (i % 5) switch
                    {
                        0 => "Food",
                        1 => "RawResource",
                        2 => "Manufactured",
                        3 => "Medicine",
                        _ => "Other"
                    },
                    Count = (i + 1) * 10,
                    IsPerishable = i % 5 == 0,
                    AverageHitPoints = 100f
                })
                .ToList();
        }

        private static List<BuildingSnapshot> BuildBuildings(int count)
        {
            return Enumerable.Range(0, count)
                .Select(i => new BuildingSnapshot
                {
                    EntityId = $"Thing_Building{i}",
                    Label = $"Building{i}",
                    DefName = i % 3 == 0 ? "ElectricStove" : "Wall",
                    Kind = i % 3 == 0 ? "Production" : "Other",
                    X = i * 2,
                    Z = i * 2,
                    Usable = true
                })
                .ToList();
        }

        private static List<BillSnapshot> BuildBills(int count)
        {
            return Enumerable.Range(0, count)
                .Select(i => new BillSnapshot
                {
                    EntityId = $"bill_{i}",
                    Label = $"Bill{i}",
                    TableId = $"Thing_Table{i % 3}",
                    RecipeDefName = i % 2 == 0 ? "CookSimpleMeal" : "ButcherCreature",
                    RepeatMode = "RepeatForever",
                    TargetCount = 10,
                    Suspended = i % 5 == 0
                })
                .ToList();
        }

        private static List<ItemSnapshot> BuildItems(int count)
        {
            return Enumerable.Range(0, count)
                .Select(i => new ItemSnapshot
                {
                    EntityId = $"Thing_Item{i}",
                    Label = $"Item{i}",
                    DefName = i % 3 == 0 ? "Gun_Revolver" : "Apparel_Parka",
                    Quality = (i % 4) switch { 0 => "Normal", 1 => "Good", 2 => "Excellent", _ => "Legendary" },
                    StackCount = 1,
                    X = i,
                    Z = i
                })
                .ToList();
        }

        private static List<RoomSnapshot> BuildRooms(int count)
        {
            return Enumerable.Range(0, count)
                .Select(i => new RoomSnapshot
                {
                    Label = $"Room{i}",
                    CellCount = 20 + i * 10,
                    Impressiveness = 0.5f + i * 0.05f,
                    Cleanliness = 0.3f,
                    Temperature = 21f
                })
                .ToList();
        }

        private static List<FactionSnapshot> BuildFactions(int count)
        {
            return Enumerable.Range(0, count)
                .Select(i => new FactionSnapshot
                {
                    EntityId = $"faction_{i}",
                    Name = $"Faction{i}",
                    Goodwill = i * 10 - 50,
                    Hostile = i > 3,
                    CanTradeNow = i <= 3
                })
                .ToList();
        }
    }
}
