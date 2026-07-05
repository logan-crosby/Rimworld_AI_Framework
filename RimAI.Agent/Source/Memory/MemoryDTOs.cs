using System.Collections.Generic;

namespace RimAI.Agent.Memory
{
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
