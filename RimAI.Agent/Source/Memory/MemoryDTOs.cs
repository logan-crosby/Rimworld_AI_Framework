using System;
using System.Collections.Generic;

namespace RimAI.Agent.Memory
{
    // --- Planner Tier ---
    // TODO(impl): PlannerTier — enumeration mapping to the three cognition planning tiers.
    //   Reactive: 10 events, no semantic retrieval, 300 tokens — event-driven combat/medical scope.
    //   Tactical: 30 events + top 3 semantic matches, 800 tokens — work/bill/zone/designation scope.
    //   Strategic: 50 events + top 10 semantic matches, 1,500 tokens — research/trade/caravan scope.
    public enum PlannerTier
    {
        Reactive,       // TODO(impl): 10 events, no semantic retrieval,     300 tokens
        Tactical,       // TODO(impl): 30 events + top 3 semantic matches,   800 tokens
        Strategic       // TODO(impl): 50 events + top 10 semantic matches,  1,500 tokens
    }

    // --- Working Memory Context ---
    // TODO(impl): WorkingMemoryContext — assembled context returned by IWorkingMemory.AssembleAsync.
    //   PromptPrefix is the assembled text for the LLM system message.
    //   ReferencedEpisodeIds tracks which episodes were used, for future context window management.
    public class WorkingMemoryContext
    {
        // TODO(impl): PromptPrefix — assembled text for LLM system message
        public string PromptPrefix { get; init; }

        // TODO(impl): EstimatedTokens — approximate token count of the assembled context
        public int EstimatedTokens { get; init; }

        // TODO(impl): ReferencedEpisodeIds — Episode.Id values included in this context
        public List<long> ReferencedEpisodeIds { get; init; }

        // TODO(impl): AssembledAt — UTC timestamp when this context was assembled
        public DateTime AssembledAt { get; init; }
    }

    // --- Memory Retrieval Result ---
    // TODO(impl): MemoryRetrievalResult — a single result from IMemoryRetrieval.SearchAsync.
    //   Contains the matched episode and its cosine similarity score (0–1).
    public class MemoryRetrievalResult
    {
        // TODO(impl): Episode — the matched episodic entry from the event log
        public EpisodicEntry Episode { get; init; }

        // TODO(impl): Similarity — cosine similarity score, 0–1 range
        public float Similarity { get; init; }
    }
    /// <summary>
    /// Atomic memory event recorded from Perception events or snapshot diffs.
    /// Appended to the episodic event log on the main thread.
    /// </summary>
    public class MemoryEvent
    {
        // TODO(impl): Tick — Find.TickManager.TicksAbs at event time
        public int Tick { get; init; }

        // TODO(impl): GameTime — GenLocalDate.DayOfQuadrum + fraction at event time
        public float GameTime { get; init; }

        // TODO(impl): EventType — PerceptionEventType.ToString() or "SnapshotDelta"
        public string EventType { get; init; }

        // TODO(impl): Description — human-readable 1-3 sentence English from template engine
        public string Description { get; init; }

        // TODO(impl): Metadata — type-specific key-value pairs from PerceptionEvent.Metadata
        public Dictionary<string, string> Metadata { get; init; }
    }

    /// <summary>
    /// Persisted episodic entry with full embedding vector for semantic retrieval.
    /// Stored in the episodic event log with configurable capacity (default 5000).
    /// </summary>
    public class EpisodicEntry
    {
        // TODO(impl): Id — monotonically increasing SequenceId, unique per session
        public long Id { get; init; }

        // TODO(impl): Tick — game tick when the event occurred
        public int Tick { get; init; }

        // TODO(impl): GameTime — game day + fraction when the event occurred
        public float GameTime { get; init; }

        // TODO(impl): EventType — PerceptionEventType.ToString() or "SnapshotDelta"
        public string EventType { get; init; }

        // TODO(impl): Description — human-readable 1-3 sentence English summary
        public string Description { get; init; }

        // TODO(impl): Embedding — float[] vector, null until async embedding task completes.
        //   Dimension-agnostic: works with 1536-dim (text-embedding-3-small) or 3072-dim.
        //   Normalized at storage for efficient cosine similarity (dot product).
        public float[] Embedding { get; init; }
    }

    /// <summary>
    /// Short-term situational awareness window injected into every LLM prompt.
    /// Maintains recent decisions, pending actions, and active goals for the planner.
    /// </summary>
    public class WorkingMemory
    {
        // TODO(impl): RecentDecisions — last N DecisionLogEntry items from the decision log,
        //   newest first. Populated by Orchestration layer after command execution feedback.
        public List<Orchestration.DecisionLogEntry> RecentDecisions { get; init; }

        // TODO(impl): PendingActions — commands queued but not yet executed (from ActionExecutor queue).
        //   Used by Cognition to avoid issuing duplicate or conflicting commands.
        public List<Action.ActionCommand> PendingActions { get; init; }

        // TODO(impl): ActiveGoals — strategic goals set by the planner (e.g., "research geothermal power").
        //   Persisted across sessions; used by Strategic tier for long-term planning.
        public List<string> ActiveGoals { get; init; }

        // TODO(impl): LastStrategicPlanTick — game tick when the last strategic plan was generated.
        //   Used by CadenceScheduler to determine when next strategic plan is due.
        public int LastStrategicPlanTick { get; init; }

        // TODO(impl): LastTacticalPlanTick — game tick when the last tactical plan was generated.
        //   Used by CadenceScheduler to determine when next tactical plan is due.
        public int LastTacticalPlanTick { get; init; }
    }
}
