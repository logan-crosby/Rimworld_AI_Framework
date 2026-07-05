using System.Threading.Tasks;

namespace RimAI.Agent.Memory
{
    /// <summary>
    /// Primary memory store interface. Provides episodic logging, embedding-based retrieval,
    /// working memory context assembly, and save/load persistence.
    /// </summary>
    public interface IMemoryStore
    {
        // TODO(impl): AppendEvent — main-thread only. Creates a MemoryEvent from incoming game event,
        //   assigns monotonically increasing SequenceId, generates NaturalSummary via template engine,
        //   enqueues for async embedding generation. Triggers capacity eviction if > 5000 episodes.
        void AppendEvent(MemoryEvent e);

        // TODO(impl): QueryByEmbeddingAsync — async semantic search using embedding similarity.
        //   Generates query embedding via RimAIApi.GetEmbeddingsAsync, computes cosine similarity
        //   against stored episode embeddings, returns top-k results sorted by similarity desc.
        //   Returns empty array if embedding store not available (no API key).
        Task<EpisodicEntry[]> QueryByEmbeddingAsync(string query, int k = 5);

        // TODO(impl): GetWorkingMemory — returns current working memory snapshot.
        //   Includes recent decisions, pending actions, active goals, and last plan ticks.
        //   Thread-safe read from any thread.
        WorkingMemory GetWorkingMemory();

        // TODO(impl): ExposeData — persists all episodic entries and embedding vectors
        //   via RimWorld Scribe (LookMode.Deep for episodes, parallel lists for embeddings).
        //   Called from MemoryGameComponent.ExposeData(). On load, rebuilds indexes and embedding cache.
        void ExposeData();
    }
}
