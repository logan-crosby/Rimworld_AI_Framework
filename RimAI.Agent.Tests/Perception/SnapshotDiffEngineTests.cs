using System.Collections.Generic;
using System.Linq;
using RimAI.Agent.Perception;
using Xunit;

namespace RimAI.Agent.Tests.Perception
{
    /// <summary>
    /// Unit tests for SnapshotDiffEngine — pure logic, testable on net8.0.
    /// </summary>
    public class SnapshotDiffEngineTests
    {
        // ── Compute: null handling ──────────────────────────────────────────

        [Fact]
        public void Compute_NullCurrent_ReturnsMinimalDiff()
        {
            var prev = BuildSnapshot(1000, pawnCount: 2, resourceCount: 3);
            var diff = SnapshotDiffEngine.Compute(prev, null);

            Assert.NotNull(diff);
            Assert.Empty(diff.AddedPawns);
            Assert.Empty(diff.RemovedPawns);
            Assert.Empty(diff.ChangedPawns);
            Assert.Empty(diff.ResourceChanges);
            Assert.False(diff.ThreatLevelChanged);
        }

        [Fact]
        public void Compute_NullPrev_AllPawnsAdded()
        {
            var current = BuildSnapshot(2000, pawnCount: 3, resourceCount: 5);

            var diff = SnapshotDiffEngine.Compute(null, current);

            Assert.NotNull(diff);
            Assert.Equal(3, diff.AddedPawns.Count);
            Assert.Contains("pawn_0", diff.AddedPawns);
            Assert.Contains("pawn_1", diff.AddedPawns);
            Assert.Contains("pawn_2", diff.AddedPawns);
            Assert.Empty(diff.RemovedPawns);
            Assert.Empty(diff.ChangedPawns);

            // All resources show as net gains from zero
            Assert.Equal(5, diff.ResourceChanges.Count);
            foreach (var rc in diff.ResourceChanges)
            {
                Assert.Equal(0, rc.OldCount);
                Assert.True(rc.NewCount > 0);
            }
        }

        // ── Compute: pawn adds/removes ──────────────────────────────────────

        [Fact]
        public void Compute_AddedPawn_Detected()
        {
            var prev = BuildSnapshot(1000, pawnCount: 2, resourceCount: 0);
            var current = BuildSnapshot(2000, pawnCount: 3, resourceCount: 0);

            var diff = SnapshotDiffEngine.Compute(prev, current);

            Assert.Single(diff.AddedPawns);
            Assert.Contains("pawn_2", diff.AddedPawns);
            Assert.Empty(diff.RemovedPawns);
        }

        [Fact]
        public void Compute_RemovedPawn_Detected()
        {
            var prev = BuildSnapshot(1000, pawnCount: 3, resourceCount: 0);
            var current = BuildSnapshot(2000, pawnCount: 2, resourceCount: 0);

            var diff = SnapshotDiffEngine.Compute(prev, current);

            Assert.Empty(diff.AddedPawns);
            Assert.Single(diff.RemovedPawns);
            Assert.Contains("pawn_2", diff.RemovedPawns);
        }

        // ── Compute: pawn changes ───────────────────────────────────────────

        [Fact]
        public void Compute_ChangedPawn_DetectsMoodChange()
        {
            var prev = BuildSnapshot(1000, pawnCount: 1, resourceCount: 0);
            prev.Pawns[0].Needs = new NeedsSnapshot { Mood = 0.8f, Food = 0.5f, Rest = 0.9f };

            var current = BuildSnapshot(2000, pawnCount: 1, resourceCount: 0);
            current.Pawns[0].Needs = new NeedsSnapshot { Mood = 0.3f, Food = 0.5f, Rest = 0.9f };

            var diff = SnapshotDiffEngine.Compute(prev, current);

            Assert.Single(diff.ChangedPawns);
            Assert.Equal("pawn_0", diff.ChangedPawns[0].PawnId);
            Assert.Contains("Needs.Mood", diff.ChangedPawns[0].ChangedFields);
        }

        [Fact]
        public void Compute_ChangedPawn_DetectsDownedState()
        {
            var prev = BuildSnapshot(1000, pawnCount: 1, resourceCount: 0);
            prev.Pawns[0].IsDowned = false;

            var current = BuildSnapshot(2000, pawnCount: 1, resourceCount: 0);
            current.Pawns[0].IsDowned = true;

            var diff = SnapshotDiffEngine.Compute(prev, current);

            Assert.Single(diff.ChangedPawns);
            Assert.Contains("IsDowned", diff.ChangedPawns[0].ChangedFields);
        }

        [Fact]
        public void Compute_ChangedPawn_PositionChange_Detected()
        {
            var prev = BuildSnapshot(1000, pawnCount: 1, resourceCount: 0);
            prev.Pawns[0].Position = new PositionSnapshot { X = 10, Z = 20, RoomLabel = "Barracks" };

            var current = BuildSnapshot(2000, pawnCount: 1, resourceCount: 0);
            current.Pawns[0].Position = new PositionSnapshot { X = 30, Z = 40, RoomLabel = "Kitchen" };

            var diff = SnapshotDiffEngine.Compute(prev, current);

            Assert.Single(diff.ChangedPawns);
            Assert.Contains("Position", diff.ChangedPawns[0].ChangedFields);
            Assert.Contains("Position.RoomLabel", diff.ChangedPawns[0].ChangedFields);
        }

        // ── Compute: identical snapshots ────────────────────────────────────

        [Fact]
        public void Compute_IdenticalSnapshots_NoChanges()
        {
            var prev = BuildSnapshot(1000, pawnCount: 2, resourceCount: 3);
            var current = BuildSnapshot(2000, pawnCount: 2, resourceCount: 3);
            // Same pawns, same resources, same counts

            var diff = SnapshotDiffEngine.Compute(prev, current);

            Assert.Empty(diff.AddedPawns);
            Assert.Empty(diff.RemovedPawns);
            Assert.Empty(diff.ChangedPawns);
            Assert.Empty(diff.ResourceChanges);
            Assert.False(diff.ThreatLevelChanged);
        }

        // ── Compute: resource deltas ────────────────────────────────────────

        [Fact]
        public void Compute_ResourceIncrease_PositiveDelta()
        {
            var prev = BuildSnapshot(1000, pawnCount: 0, resourceCount: 1);
            prev.Resources[0].ThingDef = "Wood";
            prev.Resources[0].Count = 100;

            var current = BuildSnapshot(2000, pawnCount: 0, resourceCount: 1);
            current.Resources[0].ThingDef = "Wood";
            current.Resources[0].Count = 150;

            var diff = SnapshotDiffEngine.Compute(prev, current);

            Assert.Single(diff.ResourceChanges);
            Assert.Equal("Wood", diff.ResourceChanges[0].ThingDef);
            Assert.Equal(100, diff.ResourceChanges[0].OldCount);
            Assert.Equal(150, diff.ResourceChanges[0].NewCount);
        }

        [Fact]
        public void Compute_ResourceDecrease_NegativeDelta()
        {
            var prev = BuildSnapshot(1000, pawnCount: 0, resourceCount: 1);
            prev.Resources[0].ThingDef = "Steel";
            prev.Resources[0].Count = 200;

            var current = BuildSnapshot(2000, pawnCount: 0, resourceCount: 1);
            current.Resources[0].ThingDef = "Steel";
            current.Resources[0].Count = 50;

            var diff = SnapshotDiffEngine.Compute(prev, current);

            Assert.Single(diff.ResourceChanges);
            Assert.Equal(200, diff.ResourceChanges[0].OldCount);
            Assert.Equal(50, diff.ResourceChanges[0].NewCount);
        }

        [Fact]
        public void Compute_NewResource_Detected()
        {
            var prev = BuildSnapshot(1000, pawnCount: 0, resourceCount: 0);
            var current = BuildSnapshot(2000, pawnCount: 0, resourceCount: 1);
            current.Resources[0].ThingDef = "Medicine";
            current.Resources[0].Count = 25;

            var diff = SnapshotDiffEngine.Compute(prev, current);

            Assert.Single(diff.ResourceChanges);
            Assert.Equal("Medicine", diff.ResourceChanges[0].ThingDef);
            Assert.Equal(0, diff.ResourceChanges[0].OldCount);
            Assert.Equal(25, diff.ResourceChanges[0].NewCount);
        }

        [Fact]
        public void Compute_DepletedResource_NegativeDelta()
        {
            var prev = BuildSnapshot(1000, pawnCount: 0, resourceCount: 1);
            prev.Resources[0].ThingDef = "Components";
            prev.Resources[0].Count = 10;

            var current = BuildSnapshot(2000, pawnCount: 0, resourceCount: 0);
            // Resource not in current

            var diff = SnapshotDiffEngine.Compute(prev, current);

            Assert.Single(diff.ResourceChanges);
            Assert.Equal("Components", diff.ResourceChanges[0].ThingDef);
            Assert.Equal(10, diff.ResourceChanges[0].OldCount);
            Assert.Equal(0, diff.ResourceChanges[0].NewCount);
        }

        // ── Compute: threat changes ─────────────────────────────────────────

        [Fact]
        public void Compute_ThreatLevelChanged_WhenHostilesAppear()
        {
            var prev = BuildSnapshot(1000, pawnCount: 0, resourceCount: 0);
            prev.Threats = new ThreatSnapshot { HostilePawnsOnMap = 0 };

            var current = BuildSnapshot(2000, pawnCount: 0, resourceCount: 0);
            current.Threats = new ThreatSnapshot { HostilePawnsOnMap = 5 };

            var diff = SnapshotDiffEngine.Compute(prev, current);

            Assert.True(diff.ThreatLevelChanged);
        }

        [Fact]
        public void Compute_ThreatLevelUnchanged_WhenSame()
        {
            var prev = BuildSnapshot(1000, pawnCount: 0, resourceCount: 0);
            prev.Threats = new ThreatSnapshot
            {
                HostilePawnsOnMap = 0,
                DownedColonists = 0,
                FiresNearBase = 0,
                BreachWallCandidates = 0,
                AnyMentalBreakRisk = false
            };

            var current = BuildSnapshot(2000, pawnCount: 0, resourceCount: 0);
            current.Threats = new ThreatSnapshot
            {
                HostilePawnsOnMap = 0,
                DownedColonists = 0,
                FiresNearBase = 0,
                BreachWallCandidates = 0,
                AnyMentalBreakRisk = false
            };

            var diff = SnapshotDiffEngine.Compute(prev, current);
            Assert.False(diff.ThreatLevelChanged);
        }

        // ── Summarize ───────────────────────────────────────────────────────

        [Fact]
        public void Summarize_EmptyDiff_ReturnsNoChangeMessage()
        {
            var diff = new SnapshotDiff
            {
                AddedPawns = new List<string>(),
                RemovedPawns = new List<string>(),
                ChangedPawns = new List<PawnDelta>(),
                ResourceChanges = new List<ResourceDelta>(),
                ThreatLevelChanged = false
            };

            var summary = SnapshotDiffEngine.Summarize(diff);

            Assert.Contains("No significant changes", summary);
        }

        [Fact]
        public void Summarize_NullDiff_ReturnsMessage()
        {
            var summary = SnapshotDiffEngine.Summarize(null);
            Assert.Contains("No changes", summary);
        }

        [Fact]
        public void Summarize_AddedPawns_IncludesCount()
        {
            var diff = new SnapshotDiff
            {
                AddedPawns = new List<string> { "pawn_a", "pawn_b" },
                RemovedPawns = new List<string>(),
                ChangedPawns = new List<PawnDelta>(),
                ResourceChanges = new List<ResourceDelta>(),
                ThreatLevelChanged = false
            };

            var summary = SnapshotDiffEngine.Summarize(diff);

            Assert.Contains("2 pawns arrived", summary);
        }

        [Fact]
        public void Summarize_SingleAddedPawn_UsesSingular()
        {
            var diff = new SnapshotDiff
            {
                AddedPawns = new List<string> { "pawn_x" },
                RemovedPawns = new List<string>(),
                ChangedPawns = new List<PawnDelta>(),
                ResourceChanges = new List<ResourceDelta>(),
                ThreatLevelChanged = false
            };

            var summary = SnapshotDiffEngine.Summarize(diff);

            Assert.Contains("1 pawn arrived", summary);
        }

        [Fact]
        public void Summarize_RemovedPawns_IncludesCount()
        {
            var diff = new SnapshotDiff
            {
                AddedPawns = new List<string>(),
                RemovedPawns = new List<string> { "pawn_d" },
                ChangedPawns = new List<PawnDelta>(),
                ResourceChanges = new List<ResourceDelta>(),
                ThreatLevelChanged = false
            };

            var summary = SnapshotDiffEngine.Summarize(diff);

            Assert.Contains("1 pawn left", summary);
        }

        [Fact]
        public void Summarize_ThreatLevelChanged_MentionsThreat()
        {
            var diff = new SnapshotDiff
            {
                AddedPawns = new List<string>(),
                RemovedPawns = new List<string>(),
                ChangedPawns = new List<PawnDelta>(),
                ResourceChanges = new List<ResourceDelta>(),
                ThreatLevelChanged = true
            };

            var summary = SnapshotDiffEngine.Summarize(diff);

            Assert.Contains("threat level changed", summary);
        }

        [Fact]
        public void Summarize_ResourceChange_IncludesDetail()
        {
            var diff = new SnapshotDiff
            {
                AddedPawns = new List<string>(),
                RemovedPawns = new List<string>(),
                ChangedPawns = new List<PawnDelta>(),
                ResourceChanges = new List<ResourceDelta>
                {
                    new ResourceDelta
                    {
                        ThingDef = "Wood",
                        OldCount = 100,
                        NewCount = 250
                    }
                },
                ThreatLevelChanged = false
            };

            var summary = SnapshotDiffEngine.Summarize(diff);

            Assert.Contains("Wood increased by 150", summary);
        }

        [Fact]
        public void NarrativeSummary_IsPopulatedByCompute()
        {
            var prev = BuildSnapshot(1000, pawnCount: 1, resourceCount: 1);
            var current = BuildSnapshot(2000, pawnCount: 2, resourceCount: 1);
            current.Pawns.Add(BuildPawn("pawn_new", "NewGuy"));

            var diff = SnapshotDiffEngine.Compute(prev, current);

            Assert.NotNull(diff.NarrativeSummary);
            Assert.NotEmpty(diff.NarrativeSummary);
            Assert.Contains("pawn", diff.NarrativeSummary.ToLower());
        }

        // ── Test helpers ────────────────────────────────────────────────────

        private static ColonySnapshot BuildSnapshot(
            int tick, int pawnCount, int resourceCount)
        {
            var pawns = new List<PawnSnapshot>();
            for (int i = 0; i < pawnCount; i++)
            {
                pawns.Add(BuildPawn($"pawn_{i}", $"Pawn{i}"));
            }

            var resources = new List<ResourceSnapshot>();
            for (int i = 0; i < resourceCount; i++)
            {
                resources.Add(new ResourceSnapshot
                {
                    ThingDef = $"Resource_{i}",
                    Category = "RawResource",
                    Count = (i + 1) * 20
                });
            }

            return new ColonySnapshot
            {
                Version = 2,
                Tick = tick,
                Pawns = pawns,
                Resources = resources,
                Threats = new ThreatSnapshot
                {
                    HostilePawnsOnMap = 0,
                    DownedColonists = 0,
                    FiresNearBase = 0,
                    BreachWallCandidates = 0
                }
            };
        }

        private static PawnSnapshot BuildPawn(
            string entityId,
            string label,
            bool isColonist = false)
        {
            return new PawnSnapshot
            {
                EntityId = entityId,
                Label = label,
                KindDef = "Human",
                FactionName = null,
                IsColonist = isColonist,
                IsPrisoner = false,
                IsDowned = false,
                IsDrafted = false,
                Needs = new NeedsSnapshot
                {
                    Mood = 0.7f,
                    Food = 0.8f,
                    Rest = 0.9f
                },
                Health = new HealthSnapshot
                {
                    Consciousness = 1f,
                    BloodLoss = 0f,
                    Pain = 0f,
                    OpenWoundCount = 0,
                    MissingPartCount = 0
                },
                Position = new PositionSnapshot
                {
                    X = 50,
                    Z = 50,
                    RoomLabel = "DiningRoom"
                }
            };
        }
    }
}
