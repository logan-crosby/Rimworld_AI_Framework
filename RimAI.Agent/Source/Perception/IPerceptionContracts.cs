using System.Collections.Generic;

namespace RimAI.Agent.Perception
{
    /// <summary>
    /// Main-thread snapshot-taking bridge from game state to AI pipeline.
    ///
    /// TakeSnapshot reads Verse/Pawn/Map, produces ColonySnapshot DTO.
    /// DiffSnapshots computes delta between two snapshots.
    ///
    /// All Verse types stay inside implementation; callers only see DTOs.
    /// Snapshot budget ~5K tokens (ceiling 5,500) per G-02.
    /// Schema v2 includes Buildings, Zones, Bills, NotableItems, Policies, Factions.
    /// </summary>
    public interface IPerceptionEngine
    {
        /// <summary>
        /// Take a read-only main-thread snapshot from Find.AnyPlayerHomeMap.
        /// Returns ColonySnapshot with all sub-DTOs populated.
        /// Handles empty colony (all lists empty, not null).
        /// Caps lists: 8 colonists + 5 hostiles + 5 animals max;
        /// resources 30 max; buildings 20; bills 20; items 15; factions 8; rooms 5.
        /// Null fields omitted by JSON serialization.
        /// </summary>
        ColonySnapshot TakeSnapshot(ColonySnapshot prev);

        /// <summary>
        /// Compute delta between two snapshots. Identifies added/removed/changed
        /// pawns and resource deltas. Uses int tick values; delegates to
        /// SnapshotDiffEngine.Compute.
        /// </summary>
        SnapshotDiff DiffSnapshots(ColonySnapshot prev, ColonySnapshot current);
    }
}
