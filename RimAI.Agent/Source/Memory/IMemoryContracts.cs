using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace RimAI.Agent.Memory
{
    /// <summary>
    /// Semantic similarity search across past episodes.
    /// Consumed by Cognition layer for retrieving relevant historical context.
    /// </summary>
    // TODO(impl): IMemoryRetrieval — async semantic search using stored episode embeddings.
    //   Generates query embedding via RimAIApi.GetEmbeddingsAsync, computes cosine similarity
    //   against stored episode embeddings, returns top-k results sorted by similarity desc.
    //   Returns empty list if embedding store not available (no API key, IsAvailable=false).
    public interface IMemoryRetrieval
    {
        // TODO(impl): SearchAsync — primary semantic retrieval endpoint.
        //   Generates query embedding via RimAIApi.GetEmbeddingsAsync, computes cosine similarity,
        //   returns top-k results sorted by similarity desc. Returns empty list if embeddings unavailable.
        Task<List<MemoryRetrievalResult>> SearchAsync(string query, int topK = 5, CancellationToken ct = default);

        // TODO(impl): SearchByDateRangeAsync — like SearchAsync but filtered to episode date range.
        //   Useful for retrieving events during specific game periods (e.g., "last winter").
        //   fromDay/toDay are game-time day-of-quadrum + fraction values.
        Task<List<MemoryRetrievalResult>> SearchByDateRangeAsync(
            string query, float fromDay, float toDay, int topK = 5, CancellationToken ct = default);

        // TODO(impl): IsAvailable — false if no API key or embedding feature disabled in settings.
        //   Allows callers to gracefully degrade when embeddings aren't available.
        bool IsAvailable { get; }
    }

    /// <summary>
    /// Assembles tier-specific context for LLM prompts from episodic log + semantic retrieval.
    /// Consumed by Cognition layer for constructing planner prompts.
    /// </summary>
    // TODO(impl): IWorkingMemory — builds a WorkingMemoryContext combining episodic log window,
    //   semantic retrieval results, working memory state (recent decisions, pending actions, active goals),
    //   and the current snapshot summary into a formatted prefix for the LLM system message.
    //   Tier-specific: Reactive gets 10 events, Tactical 30 + top 3 semantic, Strategic 50 + top 10 semantic.
    public interface IWorkingMemory
    {
        // TODO(impl): AssembleAsync — builds context for a planner tier.
        //   Selects episodic log window size per tier, calls IMemoryRetrieval for semantic matches,
        //   formats into a coherent prompt prefix with recent decisions/goals/actions.
        //   Respects maxTokens budget; truncates oldest events first if over budget.
        //   Returns WorkingMemoryContext with assembled text and referenced episode IDs.
        Task<WorkingMemoryContext> AssembleAsync(
            PlannerTier tier, string currentSnapshotSummary,
            int maxTokens, CancellationToken ct = default);
    }
}
