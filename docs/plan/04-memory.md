# 04 — Memory Layer Spec

**Status:** authoritative implementation spec for M5
**Namespace:** `RimAI.Agent.Memory`
**Depends on:** RimAI.Framework (`RimAIApi.GetEmbeddingsAsync`), Perception (`EventBus`, `SnapshotDiff`)
**Start from:** `RimAI.Agent/Source/Memory/` skeleton stubs (`// TODO(impl)` contracts)

---

## 1. Purpose

Long-term recall + short-term situational awareness for the AI agent. Three stores: **episodic event log** (append-only chronology), **semantic vector store** (embedding retrieval via `RimAIApi.GetEmbeddingsAsync`), and **working-memory window** (bounded recent context injected into every LLM prompt). Persisted via RimWorld `ExposeData`. This is the **only layer** in `RimAI.Agent` that directly calls `RimAIApi`.

---

## 2. Episodic Event Log

### 2.1 Episode DTO

```csharp
namespace RimAI.Agent.Memory {
    public class Episode {
        public long SequenceId { get; init; }         // monotonically increasing per session
        public int GameTick { get; init; }            public float GameDay { get; init; }
        public string EventType { get; init; }        // PerceptionEventType.ToString() or "SnapshotDelta"
        public string SourcePawnId { get; init; }     // null = map-level
        public string NaturalSummary { get; init; }   // 1–3 sentence English, produced by template engine
        public string JsonPayload { get; init; }      // minified JSON of raw event or diff
        public CriticalLevel Importance { get; init; } // from PerceptionEvent.Criticality
        public float[] Embedding { get; set; }        // null until async embedding task completes
        public bool EmbeddingStored { get; set; }     // false until persisted
    }
}
```

**`NaturalSummary`** produced by local template engine (no LLM call). Example: `"Colonist 'Rynyk' died from a gunshot wound to the head during a tribal raid at Day 12.3."`

### 2.2 EventLog Store

```csharp
public class EventLog {
    public Episode Append(PerceptionEvent e);               // main-thread only
    public Episode AppendSnapshotDiff(SnapshotDiff diff);    // main-thread only
    public List<Episode> GetRecent(int count);               // thread-safe read
    public List<Episode> GetByDateRange(float fromDay, float toDay);
    public List<Episode> GetByType(string eventType);
    public int Count { get; }
    public void ExposeData();                               // called from MemoryGameComponent.ExposeData()
}
```

**Capacity:** capped at **5,000 episodes**. At 5,001+: oldest 1,000 compressed into one `"Legacy summary"` episode before removal — agent retains compressed very-old history.

### 2.3 Summary Template Engine

```csharp
public static class SummaryTemplateEngine {
    public static string Summarize(PerceptionEvent e);       // e.g., "{Name} died at Day {Day}..."
    public static string SummarizeDiff(SnapshotDiff diff);  // e.g., "Steel -45, Wood +120 at Day 15.2"
}
```

Template map covers all 12 `PerceptionEventType` values. Placeholders resolved from `PerceptionEvent` fields + `Metadata` dict.

---

## 3. Semantic Memory (Embedding Store)

### 3.1 Retrieval API — consumed by Cognition

```csharp
namespace RimAI.Agent.Memory {
    public class MemoryRetrievalResult {
        public Episode Episode { get; init; }  public float Similarity { get; init; }  // cosine 0–1
    }
    public interface IMemoryRetrieval {
        Task<List<MemoryRetrievalResult>> SearchAsync(string query, int topK=5, CancellationToken ct=default);
        Task<List<MemoryRetrievalResult>> SearchByDateRangeAsync(
            string query, float fromDay, float toDay, int topK=5, CancellationToken ct=default);
        bool IsAvailable { get; }  // false if no API key or embedding disabled
    }
}
```

### 3.2 EmbeddingStore Implementation

```csharp
public class EmbeddingStore : IMemoryRetrieval {
    public void EnqueueForEmbedding(Episode episode);             // fire-and-forget
    public async Task ProcessPendingAsync(CancellationToken ct);  // batch ≤20; MUST run off-thread
    public void RebuildCache();   // repopulate ConcurrentDictionary<long, float[]> after ExposeData load
    public void ExposeData();     // persist all embeddings
}
```

### 3.3 Lifecycle

```
Episode created (main thread)
    │
    ▼
NaturalSummary enqueued → ConcurrentQueue<(Episode, retryCount)>
    │
    ▼  (off-thread, driven every ~250 ticks by orchestrator)
1. Dequeue ≤20 summaries
2. UnifiedEmbeddingRequest { Inputs = summaries }
3. Result<UnifiedEmbeddingResponse> = await RimAIApi.GetEmbeddingsAsync(request, ct)
4. Success: episode.Embedding = result.Value.Data[i].Embedding; EmbeddingStored=true;
             cache normalize and store
5. Failure: re-enqueue retryCount++; max 3 retries; then log warning + skip
```

**Dimension-agnostic:** works with 1,536-dim (`text-embedding-3-small`) or 3,072-dim. Vectors normalized at storage; `CosineSimilarity` = dot product. Batch size 20 (within API input limits). Fresh colony ≈10 episodes/game-day → one batch ≈2 days.

### 3.4 Vector Math

```csharp
public static class VectorMath {
    public static float CosineSimilarity(float[] a, float[] b); // throws if lengths differ
    public static float[] Normalize(float[] vector);
}
```

---

## 4. Working Memory Window

### 4.1 Interface — consumed by Cognition

```csharp
namespace RimAI.Agent.Memory {
    public enum PlannerTier {
        Reactive,       // 10 events, no semantic retrieval,     300 tokens
        Tactical,       // 30 events + top 3 semantic matches,   800 tokens
        Strategic       // 50 events + top 10 semantic matches,  1,500 tokens
    }
    public class WorkingMemoryContext {
        public string PromptPrefix { get; init; }              // assembled text for LLM system message
        public int EstimatedTokens { get; init; }
        public List<long> ReferencedEpisodeIds { get; init; }
        public DateTime AssembledAt { get; init; }
    }
    public interface IWorkingMemory {
        Task<WorkingMemoryContext> AssembleAsync(
            PlannerTier tier, string currentSnapshotSummary,
            int maxTokens, CancellationToken ct = default);
    }
}
```

### 4.2 Context Template

```
[Working Memory — {tier} Tier — Day {gameDay} {season}]

CURRENT SITUATION:
{snapshotSummary}                                          ← never trimmed

RECENT EVENTS (last N):
- Day 12.3: Colonist 'Rynyk' died from gunshot... [high]   ← oldest dropped first
- Day 12.2: Raid: 5 tribal raiders from Cleftspine [high]

RELEVANT MEMORIES (semantic search):                      ← Tactical/Strategic only
- Day 5.7: Tribal raid repelled using killbox... (0.87)

KEY COLONISTS:
- Rynyk (deceased), Aryk (shooting 12, injured), Bluel (cooking 8, healthy)...

GOALS & DECISIONS:                                         ← Strategic only
- Strategic goal: research geothermal power [Day 10.2]
```

### 4.3 Assembler Implementation

```csharp
public class WorkingMemoryAssembler : IWorkingMemory {
    private readonly EventLog _eventLog;
    private readonly IMemoryRetrieval _retrieval;
    public WorkingMemoryAssembler(EventLog eventLog, IMemoryRetrieval retrieval);

    // Assembly order:
    // 1. Fetch N recent episodes (tier-dependent)
    // 2. Tactical/Strategic: SearchAsync(query from snapshotSummary + recent types)
    // 3. Strategic: include prior goals from "Decision"/"Goal" episode types
    // 4. Render sections; trim from bottom if over budget
    // 5. Return WorkingMemoryContext with token estimate

    private string TrimToBudget(string assembled, int maxTokens);
    // Trim: oldest episodes → fewer semantic results → colonist details → goals.
    // NEVER truncates CURRENT SITUATION. Targets 90% of maxTokens for headroom.
}
```

Token estimate: `CharCount / 4` (rough). Trimmer targets 90% of budget for actual tokenization variance.

---

## 5. ExposeData Persistence

```csharp
namespace RimAI.Agent.Memory {
    public class MemoryGameComponent : GameComponent {
        public EventLog EventLog { get; private set; }
        public EmbeddingStore EmbeddingStore { get; private set; }

        public MemoryGameComponent(Game game) : base(game);

        public override void ExposeData() {
            // 1. Scribe Deep: List<Episode> (all fields incl. Embedding, EmbeddingStored)
            Scribe_Collections.Look(ref _episodesList, "memoryEpisodes", LookMode.Deep, ref _targets);
            // 2. Scribe parallel lists: List<long> keys + List<float[]> values
            Scribe_Collections.Look(ref _embedDict, "memoryEmbeddings", LookMode.Value, LookMode.Value,
                ref _seqIdList, ref _vectorList);
            // 3. On load: EventLog.RebuildIndexes(); EmbeddingStore.RebuildCache();
            base.ExposeData();
        }
        // Boxing fields for Scribe compatibility
        private List<Episode> _episodesList;  private object[] _targets;
        private Dictionary<long, float[]> _embedDict;
        private List<long> _seqIdList;  private List<float[]> _vectorList;
    }
}
```

**Vector cost:** 1,536-dim × 5,000 ≈ 30MB uncompressed; RimWorld XML compression → ~5–10MB. Configurable `MaxEmbeddedEpisodes` (default 2,000) drops embeddings for oldest episodes while retaining summaries.

**Save compatibility (no-mod):** `MemoryGameComponent` never created → no crash.
**Save compatibility (mod removal):** RimWorld drops unknown `GameComponent` → no crash.

---

## 6. Retrieval API Usage by Cognition

```csharp
// Inside TacticalPlanner:
var memories = await _retrieval.SearchAsync(
    $"Colony at Day {day}: {summary}", topK: 3, ct: ct);

// Inside StrategicPlanner:
var memories = await _retrieval.SearchByDateRangeAsync(
    $"Strategic decisions and threats past {window} days",
    fromDay: day - window, toDay: day, topK: 10, ct: ct);

// Every tier, before LLM call:
var ctx = await _workingMemory.AssembleAsync(
    tier, snapshotSummary, maxTokens, ct);
// ctx.PromptPrefix prepended to LLM system message
```

Cognition **never sees raw `Episode` objects** — only assembled `WorkingMemoryContext.PromptPrefix` string + `MemoryRetrievalResult` list (for logging).

---

## 7. Interface Summary

| Interface | Consumer | Purpose |
|-----------|----------|---------|
| `IMemoryRetrieval` | Cognition | Semantic similarity search across past episodes |
| `IWorkingMemory` | Cognition | Assemble tier-specific context for LLM prompts |
| `EventLog.Append()` | Perception (via `IMemoryLogger`/`EventBus`) | Record episodes from PerceptionEvents |
| `EventLog.AppendSnapshotDiff()` | Perception (via `EventBus`) | Record state changes as episodes |

`IMemoryLogger` (in `RimAI.Agent.Perception`): implements `IEventSink`; `ReceiveEvent()` → `EventLog.Append()` → `EmbeddingStore.EnqueueForEmbedding()`. Registered via `EventBus.Subscribe()` at mod init.

---

## 8. Acceptance Criteria — M5

- [ ] `EventLog.Append(PerceptionEvent)` creates `Episode` with valid `NaturalSummary` and sequential `SequenceId`.
- [ ] `SummaryTemplateEngine.Summarize()` produces correct English for all 12 `PerceptionEventType` values.
- [ ] `EmbeddingStore.ProcessPendingAsync` calls `RimAIApi.GetEmbeddingsAsync` with batched summaries (≤20).
- [ ] `SearchAsync("raid defense", topK:5)` returns episodes ranked by cosine similarity.
- [ ] `WorkingMemoryAssembler.AssembleAsync` produces context ≤`maxTokens` for each `PlannerTier`, correct sections.
- [ ] `TrimToBudget` drops oldest events first; `CURRENT SITUATION` never truncated.
- [ ] `MemoryGameComponent.ExposeData()` round-trips: save→load, all episodes+embeddings intact, `EmbeddingStored` flags correct.
- [ ] Eviction at 5,001+: oldest 1,000 compressed into legacy summary, 4,001 remain.
- [ ] Loading save without mod: no crash.
- [ ] `dotnet build Rim_AI_Framework.sln` — 0 errors, 0 warnings.
- [ ] Unit: `VectorMath.CosineSimilarity` = 1.0 identical, ≈0.0 orthogonal, ≈1.0 near-identical.
- [ ] Unit: `SummaryTemplateEngine` covers all 12 event types, non-empty output.
- [ ] Unit: `TrimToBudget(500)` correctly truncates 2,000-token input.

---

## 9. Edge Cases

1. **Embedding API unavailable** (no API key): `IsAvailable=false`; `SearchAsync` empty; `ProcessPendingAsync` no-op. Episodes accumulate with `Embedding=null`. Working memory omits semantic section.
2. **API error**: re-enqueue `retryCount++` max 3; then `EmbeddingStored=false` + log. Cache skips.
3. **Dimension mismatch**: `CosineSimilarity` throws only on different-length vectors. Store compares same-length correctly at any dimension.
4. **Zero episodes**: `SearchAsync` returns empty; `AssembleAsync` produces `"(No recorded events yet.)"`.
5. **Eviction boundary**: exactly 5,000 → none. 5,001 → compress oldest 1,000, 4,001 remain.
6. **File size cap**: `MaxEmbeddedEpisodes=2000` → 2,000 embeddings (~12MB uncompressed). Older: summaries only.
7. **Concurrent embed+search**: `ConcurrentDictionary` cache — safe. Mid-write episode may/may-not appear (acceptable).
8. **No snapshot yet** (post-load, pre-first Perception tick): `AssembleAsync` placeholder `"Colony snapshot not yet available."`.
9. **Token drift**: `charCount/4` rough; trimmer targets 90% `maxTokens`.
10. **Mod version upgrade**: `Episode.Version` scribed; migration on load for new fields; downgrade ignores unknown fields.

---

STATUS: DOCS-A PASS — 265 lines
