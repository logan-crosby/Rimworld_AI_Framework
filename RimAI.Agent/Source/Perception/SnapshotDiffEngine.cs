using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace RimAI.Agent.Perception
{
    /// <summary>
    /// Computes the delta between two ColonySnapshot DTOs and generates
    /// a human-readable narrative summary. Pure logic — no Verse/RimWorld types.
    /// </summary>
    public static class SnapshotDiffEngine
    {
        /// <summary>
        /// Compute diff between two snapshots. Identifies:
        /// - Added/removed pawns (by EntityId)
        /// - Changed pawns (by EntityId with list of changed field paths)
        /// - Resource count deltas (by ThingDef)
        /// - Threat level change flag
        ///
        /// If prev is null, all pawns are "added" and all resources show as
        /// net gains from zero.
        /// </summary>
        public static SnapshotDiff Compute(ColonySnapshot prev, ColonySnapshot current)
        {
            if (current == null)
            {
                return new SnapshotDiff
                {
                    FromTick = prev?.Tick ?? 0,
                    ToTick = 0,
                    AddedPawns = new List<string>(),
                    RemovedPawns = new List<string>(),
                    ChangedPawns = new List<PawnDelta>(),
                    ResourceChanges = new List<ResourceDelta>(),
                    ThreatLevelChanged = false,
                    NarrativeSummary = "No current snapshot available."
                };
            }

            var diff = new SnapshotDiff
            {
                FromTick = prev?.Tick ?? 0,
                ToTick = current.Tick,
                AddedPawns = new List<string>(),
                RemovedPawns = new List<string>(),
                ChangedPawns = new List<PawnDelta>(),
                ResourceChanges = new List<ResourceDelta>(),
                ThreatLevelChanged = false
            };

            // First snapshot: everything is "added"
            if (prev == null)
            {
                if (current.Pawns != null)
                {
                    foreach (var pawn in current.Pawns)
                    {
                        if (!string.IsNullOrEmpty(pawn.EntityId))
                            diff.AddedPawns.Add(pawn.EntityId);
                    }
                }

                if (current.Resources != null)
                {
                    foreach (var res in current.Resources)
                    {
                        diff.ResourceChanges.Add(new ResourceDelta
                        {
                            ThingDef = res.ThingDef,
                            OldCount = 0,
                            NewCount = res.Count
                        });
                    }
                }

                diff.NarrativeSummary = Summarize(diff);
                return diff;
            }

            // ── Compare pawns ────────────────────────────────────────────

            var prevPawnIds = new HashSet<string>(
                (prev.Pawns ?? Enumerable.Empty<PawnSnapshot>())
                    .Select(p => p.EntityId)
                    .Where(id => !string.IsNullOrEmpty(id)));

            var currPawnIds = new HashSet<string>(
                (current.Pawns ?? Enumerable.Empty<PawnSnapshot>())
                    .Select(p => p.EntityId)
                    .Where(id => !string.IsNullOrEmpty(id)));

            // Added: in current but not previous
            foreach (var id in currPawnIds)
            {
                if (!prevPawnIds.Contains(id))
                    diff.AddedPawns.Add(id);
            }

            // Removed: in previous but not current
            foreach (var id in prevPawnIds)
            {
                if (!currPawnIds.Contains(id))
                    diff.RemovedPawns.Add(id);
            }

            // Changed: in both, compare fields
            var currPawnMap = (current.Pawns ?? new List<PawnSnapshot>())
                .Where(p => !string.IsNullOrEmpty(p.EntityId))
                .ToDictionary(p => p.EntityId, p => p);

            var prevPawnMap = (prev.Pawns ?? new List<PawnSnapshot>())
                .Where(p => !string.IsNullOrEmpty(p.EntityId))
                .ToDictionary(p => p.EntityId, p => p);

            foreach (var pawnId in prevPawnIds.Intersect(currPawnIds))
            {
                if (currPawnMap.TryGetValue(pawnId, out var currPawn) &&
                    prevPawnMap.TryGetValue(pawnId, out var prevPawnData))
                {
                    var changedFields = ComparePawns(prevPawnData, currPawn);
                    if (changedFields.Count > 0)
                    {
                        diff.ChangedPawns.Add(new PawnDelta
                        {
                            PawnId = pawnId,
                            ChangedFields = changedFields
                        });
                    }
                }
            }

            // ── Compare resources ────────────────────────────────────────

            diff.ResourceChanges = CompareResources(prev.Resources, current.Resources);

            // ── Compare threats ──────────────────────────────────────────

            diff.ThreatLevelChanged = HasThreatLevelChanged(prev.Threats, current.Threats);

            // ── Generate narrative ───────────────────────────────────────

            diff.NarrativeSummary = Summarize(diff);

            return diff;
        }

        /// <summary>
        /// Generate a 1–2 sentence English narrative from the diff.
        /// </summary>
        public static string Summarize(SnapshotDiff diff)
        {
            if (diff == null) return "No changes to report.";

            var parts = new List<string>();

            if (diff.AddedPawns?.Count > 0)
                parts.Add($"{diff.AddedPawns.Count} pawn{(diff.AddedPawns.Count == 1 ? "" : "s")} arrived");

            if (diff.RemovedPawns?.Count > 0)
                parts.Add($"{diff.RemovedPawns.Count} pawn{(diff.RemovedPawns.Count == 1 ? "" : "s")} left");

            if (diff.ChangedPawns?.Count > 0)
                parts.Add($"{diff.ChangedPawns.Count} pawn{(diff.ChangedPawns.Count == 1 ? "" : "s")} changed");

            // Resource summary: aggregate net change
            if (diff.ResourceChanges?.Count > 0)
            {
                var increased = diff.ResourceChanges.Where(r => r.NewCount > r.OldCount).ToList();
                var decreased = diff.ResourceChanges.Where(r => r.NewCount < r.OldCount).ToList();

                if (increased.Count > 0)
                {
                    var topIncrease = increased.OrderByDescending(r => r.NewCount - r.OldCount).First();
                    parts.Add($"{topIncrease.ThingDef} increased by {topIncrease.NewCount - topIncrease.OldCount}");
                }

                if (decreased.Count > 0 && parts.Count < 2)
                {
                    var topDecrease = decreased.OrderBy(r => r.OldCount - r.NewCount).First();
                    parts.Add($"{topDecrease.ThingDef} decreased by {topDecrease.OldCount - topDecrease.NewCount}");
                }
            }

            if (diff.ThreatLevelChanged)
                parts.Add("threat level changed");

            if (parts.Count == 0)
                return "No significant changes since last snapshot.";

            var sb = new StringBuilder();
            sb.Append(string.Join("; ", parts.Take(3)));
            sb.Append('.');

            return sb.ToString();
        }

        // ── Private helpers ──────────────────────────────────────────────────

        /// <summary>
        /// Compare two PawnSnapshots and return the list of changed field paths.
        /// </summary>
        private static List<string> ComparePawns(PawnSnapshot prev, PawnSnapshot curr)
        {
            var changes = new List<string>();

            if (prev == null || curr == null) return changes;

            // Top-level bools
            if (prev.IsColonist != curr.IsColonist) changes.Add("IsColonist");
            if (prev.IsPrisoner != curr.IsPrisoner) changes.Add("IsPrisoner");
            if (prev.IsDowned != curr.IsDowned) changes.Add("IsDowned");
            if (prev.IsDrafted != curr.IsDrafted) changes.Add("IsDrafted");

            // Faction change
            if (prev.FactionName != curr.FactionName) changes.Add("FactionName");

            // Needs
            if (prev.Needs != null && curr.Needs != null)
            {
                if (Math.Abs(prev.Needs.Mood - curr.Needs.Mood) > 0.01f) changes.Add("Needs.Mood");
                if (Math.Abs(prev.Needs.Food - curr.Needs.Food) > 0.01f) changes.Add("Needs.Food");
                if (Math.Abs(prev.Needs.Rest - curr.Needs.Rest) > 0.01f) changes.Add("Needs.Rest");
                if (Math.Abs(prev.Needs.Recreation - curr.Needs.Recreation) > 0.01f) changes.Add("Needs.Recreation");
            }
            else if ((prev.Needs == null) != (curr.Needs == null))
            {
                changes.Add("Needs");
            }

            // Health
            if (prev.Health != null && curr.Health != null)
            {
                if (Math.Abs(prev.Health.Consciousness - curr.Health.Consciousness) > 0.01f) changes.Add("Health.Consciousness");
                if (Math.Abs(prev.Health.BloodLoss - curr.Health.BloodLoss) > 0.01f) changes.Add("Health.BloodLoss");
                if (Math.Abs(prev.Health.Pain - curr.Health.Pain) > 0.01f) changes.Add("Health.Pain");
                if (prev.Health.OpenWoundCount != curr.Health.OpenWoundCount) changes.Add("Health.OpenWoundCount");
                if (prev.Health.MissingPartCount != curr.Health.MissingPartCount) changes.Add("Health.MissingPartCount");
            }
            else if ((prev.Health == null) != (curr.Health == null))
            {
                changes.Add("Health");
            }

            // Position
            if (prev.Position != null && curr.Position != null)
            {
                if (prev.Position.X != curr.Position.X || prev.Position.Z != curr.Position.Z)
                    changes.Add("Position");
                if (prev.Position.RoomLabel != curr.Position.RoomLabel)
                    changes.Add("Position.RoomLabel");
            }
            else if ((prev.Position == null) != (curr.Position == null))
            {
                changes.Add("Position");
            }

            // Work
            if (prev.Work?.CurrentJobDef != curr.Work?.CurrentJobDef)
                changes.Add("Work.CurrentJobDef");

            // Equipment
            if (prev.Equipment?.WeaponLabel != curr.Equipment?.WeaponLabel)
                changes.Add("Equipment.WeaponLabel");

            return changes;
        }

        /// <summary>
        /// Compare resource lists and return deltas.
        /// Resources present in only one snapshot get default-zero counts.
        /// </summary>
        private static List<ResourceDelta> CompareResources(
            List<ResourceSnapshot> prevResources,
            List<ResourceSnapshot> currResources)
        {
            var deltas = new List<ResourceDelta>();

            var prevMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            if (prevResources != null)
            {
                foreach (var r in prevResources)
                {
                    if (!string.IsNullOrEmpty(r.ThingDef))
                        prevMap[r.ThingDef] = r.Count;
                }
            }

            var currMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            if (currResources != null)
            {
                foreach (var r in currResources)
                {
                    if (!string.IsNullOrEmpty(r.ThingDef))
                        currMap[r.ThingDef] = r.Count;
                }
            }

            // Find all unique ThingDefs
            var allDefs = new HashSet<string>(prevMap.Keys, StringComparer.OrdinalIgnoreCase);
            foreach (var key in currMap.Keys)
                allDefs.Add(key);

            foreach (var def in allDefs)
            {
                prevMap.TryGetValue(def, out var oldCount);
                currMap.TryGetValue(def, out var newCount);

                if (oldCount != newCount)
                {
                    deltas.Add(new ResourceDelta
                    {
                        ThingDef = def,
                        OldCount = oldCount,
                        NewCount = newCount
                    });
                }
            }

            return deltas;
        }

        /// <summary>
        /// Returns true if the threat snapshots differ meaningfully.
        /// </summary>
        private static bool HasThreatLevelChanged(ThreatSnapshot prev, ThreatSnapshot curr)
        {
            if (prev == null && curr == null) return false;
            if (prev == null || curr == null) return true;

            return prev.HostilePawnsOnMap != curr.HostilePawnsOnMap
                || prev.DownedColonists != curr.DownedColonists
                || prev.FiresNearBase != curr.FiresNearBase
                || prev.BreachWallCandidates != curr.BreachWallCandidates
                || prev.AnyMentalBreakRisk != curr.AnyMentalBreakRisk;
        }
    }
}
