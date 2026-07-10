using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json;

namespace RimAI.Agent.Perception
{
    /// <summary>
    /// Token-budget-aware colony snapshot trimmer.
    /// Pure logic — no Verse/RimWorld types. Operates on ColonySnapshot DTOs.
    ///
    /// Trim order (drop first, per G-02):
    ///   1. NotableItems (drop entirely)
    ///   2. Factions (drop entirely)
    ///   3. Buildings beyond 10
    ///   4. Bills beyond 10
    ///   5. Resources beyond 30
    ///   6. TopRooms beyond 3
    ///   7. Pawns beyond 8 (colonists first, then hostiles, then animals)
    ///   8. Per-pawn WorkSnapshot (set to null)
    ///   9. Per-pawn EquipSnapshot (set to null)
    ///  10. Per-pawn TopThoughts beyond 3
    ///  11. Per-pawn Skills beyond 3
    /// </summary>
    public static class ColonySnapshotTrimmer
    {
        /// <summary>Target token budget per G-02 (was 4096).</summary>
        public const int DefaultTokenBudget = 5000;

        /// <summary>Absolute ceiling per G-02. Emit warning if exceeded after all trims.</summary>
        public const int TokenBudgetCeiling = 5500;

        /// <summary>Chars-per-token heuristic for rough estimation.</summary>
        public const int CharsPerToken = 4;

        /// <summary>Maximum trim passes to prevent infinite loops.</summary>
        private const int MaxTrimPasses = 12;

        /// <summary>Shared serializer settings: compact, null fields omitted.</summary>
        public static readonly JsonSerializerSettings SerializerSettings = new JsonSerializerSettings
        {
            Formatting = Formatting.None,
            NullValueHandling = NullValueHandling.Ignore
        };

        // ── Public API ──────────────────────────────────────────────────────

        /// <summary>
        /// Rough token estimate using chars/4 heuristic.
        /// </summary>
        public static int EstimateTokens(string json)
        {
            if (string.IsNullOrEmpty(json)) return 0;
            return (int)Math.Ceiling((double)json.Length / CharsPerToken);
        }

        /// <summary>
        /// Convenience: deserialize JSON, trim DTO, re-serialize.
        /// Returns original string if already under budget or deserialization fails.
        /// </summary>
        public static string TrimToBudget(string serializedJson, int tokenBudget = DefaultTokenBudget)
        {
            if (string.IsNullOrEmpty(serializedJson)) return serializedJson;
            if (EstimateTokens(serializedJson) <= tokenBudget) return serializedJson;

            ColonySnapshot snapshot;
            try
            {
                snapshot = JsonConvert.DeserializeObject<ColonySnapshot>(serializedJson, SerializerSettings);
            }
            catch (JsonException)
            {
                // Can't parse — return as-is
                return serializedJson;
            }

            if (snapshot == null) return serializedJson;

            Trim(snapshot, tokenBudget);
            return JsonConvert.SerializeObject(snapshot, SerializerSettings);
        }

        /// <summary>
        /// Trim a ColonySnapshot DTO in-place to fit within tokenBudget.
        /// Applies trim levels progressively until under budget or all levels
        /// exhausted. Returns the same DTO (mutated in-place).
        /// Never throws.
        /// </summary>
        public static ColonySnapshot Trim(ColonySnapshot snapshot, int tokenBudget = DefaultTokenBudget)
        {
            if (snapshot == null) return null;

            // Quick check: already under budget?
            if (EstimateSnapshot(snapshot) <= tokenBudget)
                return snapshot;

            int passes = 0;

            // Level 1: Drop NotableItems entirely
            if (snapshot.NotableItems != null)
            {
                snapshot.NotableItems = null;
                passes++;
                if (EstimateSnapshot(snapshot) <= tokenBudget) return snapshot;
            }

            // Level 2: Drop Factions entirely
            if (snapshot.Factions != null)
            {
                snapshot.Factions = null;
                passes++;
                if (EstimateSnapshot(snapshot) <= tokenBudget) return snapshot;
            }

            // Level 3: Buildings beyond 10
            if (snapshot.Buildings != null && snapshot.Buildings.Count > 10)
            {
                snapshot.Buildings = snapshot.Buildings.Take(10).ToList();
                passes++;
                if (EstimateSnapshot(snapshot) <= tokenBudget) return snapshot;
            }

            // Level 4: Bills beyond 10
            if (snapshot.Bills != null && snapshot.Bills.Count > 10)
            {
                snapshot.Bills = snapshot.Bills.Take(10).ToList();
                passes++;
                if (EstimateSnapshot(snapshot) <= tokenBudget) return snapshot;
            }

            // Level 5: Resources beyond 30
            if (snapshot.Resources != null && snapshot.Resources.Count > 30)
            {
                snapshot.Resources = snapshot.Resources.Take(30).ToList();
                passes++;
                if (EstimateSnapshot(snapshot) <= tokenBudget) return snapshot;
            }

            // Level 6: TopRooms beyond 3
            if (snapshot.Map?.TopRooms != null && snapshot.Map.TopRooms.Count > 3)
            {
                snapshot.Map.TopRooms = snapshot.Map.TopRooms.Take(3).ToList();
                passes++;
                if (EstimateSnapshot(snapshot) <= tokenBudget) return snapshot;
            }

            // Level 7: Pawns beyond 8 — keep colonists, then hostiles, then animals
            if (snapshot.Pawns != null && snapshot.Pawns.Count > 8)
            {
                var sorted = snapshot.Pawns
                    .OrderByDescending(p => p.IsColonist ? 3 : p.IsPrisoner ? 2 : p.FactionName != null && p.FactionName.Contains("hostile") ? 1 : 0)
                    .Take(8)
                    .ToList();
                snapshot.Pawns = sorted;
                passes++;
                if (EstimateSnapshot(snapshot) <= tokenBudget) return snapshot;
            }

            // Levels 8–11: Per-pawn trimming
            if (snapshot.Pawns != null)
            {
                // Level 8: Drop WorkSnapshot per pawn
                bool trimmedWork = false;
                foreach (var pawn in snapshot.Pawns)
                {
                    if (pawn.Work != null)
                    {
                        pawn.Work = null;
                        trimmedWork = true;
                    }
                }
                if (trimmedWork)
                {
                    passes++;
                    if (EstimateSnapshot(snapshot) <= tokenBudget) return snapshot;
                }

                // Level 9: Drop EquipSnapshot per pawn
                bool trimmedEquip = false;
                foreach (var pawn in snapshot.Pawns)
                {
                    if (pawn.Equipment != null)
                    {
                        pawn.Equipment = null;
                        trimmedEquip = true;
                    }
                }
                if (trimmedEquip)
                {
                    passes++;
                    if (EstimateSnapshot(snapshot) <= tokenBudget) return snapshot;
                }

                // Level 10: TopThoughts beyond 3 per pawn
                bool trimmedThoughts = false;
                foreach (var pawn in snapshot.Pawns)
                {
                    if (pawn.Needs?.TopThoughts != null && pawn.Needs.TopThoughts.Count > 3)
                    {
                        pawn.Needs.TopThoughts = pawn.Needs.TopThoughts.Take(3).ToList();
                        trimmedThoughts = true;
                    }
                }
                if (trimmedThoughts)
                {
                    passes++;
                    if (EstimateSnapshot(snapshot) <= tokenBudget) return snapshot;
                }

                // Level 11: Skills beyond 3 per pawn
                bool trimmedSkills = false;
                foreach (var pawn in snapshot.Pawns)
                {
                    if (pawn.Skills?.Skills != null && pawn.Skills.Skills.Count > 3)
                    {
                        pawn.Skills.Skills = pawn.Skills.Skills.Take(3).ToList();
                        trimmedSkills = true;
                    }
                }
                if (trimmedSkills)
                {
                    passes++;
                    if (EstimateSnapshot(snapshot) <= tokenBudget) return snapshot;
                }

                // Safety: max passes to prevent infinite loops
                while (passes < MaxTrimPasses && EstimateSnapshot(snapshot) > tokenBudget)
                {
                    // Last resort: drop more pawns (down to 3 minimum)
                    if (snapshot.Pawns.Count > 3)
                    {
                        // Drop animals first
                        var toKeep = snapshot.Pawns
                            .Where(p => p.IsColonist)
                            .Concat(snapshot.Pawns.Where(p => !p.IsColonist))
                            .Take(Math.Max(3, snapshot.Pawns.Count - 1))
                            .ToList();
                        snapshot.Pawns = toKeep;
                    }
                    passes++;
                }
            }

            return snapshot;
        }

        // ── Internal helpers ─────────────────────────────────────────────────

        /// <summary>
        /// Serialize a snapshot to minified JSON and estimate token count.
        /// </summary>
        private static int EstimateSnapshot(ColonySnapshot snapshot)
        {
            if (snapshot == null) return 0;
            try
            {
                var json = JsonConvert.SerializeObject(snapshot, SerializerSettings);
                return EstimateTokens(json);
            }
            catch (JsonException)
            {
                // Fallback: rough struct-size estimate
                return int.MaxValue;
            }
        }
    }
}
