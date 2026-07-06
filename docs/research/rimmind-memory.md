# RimMind-Memory — Source Analysis

**Source repo:** `RimWorld-RimMind-Mod-Memory` (GitHub: RimWorld-RimMind-Mod)
**Commit:** `4e61b51`
**License:** MIT (code adoption allowed with attribution)
**Analyzed for:** `RimAI.Agent.Memory` (spec: `docs/plan/04-memory.md`)

---

## 1. Architecture Summary

Three-tier memory (active/archive/dark) per pawn + narrator. Harmony patches capture 6 event types → `MemoryEntry` DTOs. `WorldComponent` persists via `ExposeData()`. Work-session aggregator collapses repetitive jobs. Dark memory = daily LLM call to compress today's events into permanent impressions. Injection into prompts via provider pattern with configurable ratio caps. **No embeddings, no vector search, no semantic retrieval, no token budgeting.** Selection purely recency-based.

---

## 2. Key Patterns

### 2.1 Three-Tier Storage (Active → Archive → Dark)

Every entity (pawn, narrator) has three `List<MemoryEntry>` buckets:

```csharp
// Source/Data/PawnMemoryStore.cs:9-11
public List<MemoryEntry> active  = new List<MemoryEntry>();
public List<MemoryEntry> archive = new List<MemoryEntry>();
public List<MemoryEntry> dark    = new List<MemoryEntry>();
```

**Active:** newest-first, capacity-capped. Overflow demotes oldest non-pinned entry to archive (inserted by importance rank). **Archive:** importance-sorted descending, separately capped. **Dark:** LLM-generated long-term impressions, always pinned, never evicted.

Eviction rule (Source/Data/PawnMemoryStore.cs:19-37): non-pinned entries evicted first; archive evicts lowest-importance non-pinned.

```
Active[newest...oldest] --overflow--> Archive[highest_imp...lowest_imp] --overflow--> dropped
Dark: permanent, pinned
```

### 2.2 MemoryEntry DTO

```csharp
// Source/Data/MemoryEntry.cs:5-18
public enum MemoryType { Work, Event, Manual, Dark }

public class MemoryEntry : IExposable {
    public string  id = string.Empty;          // "mem-{tick}"
    public string  content = string.Empty;     // natural-language text
    public MemoryType type;
    public int     tick;
    public float   importance;                 // 0.0–1.0
    public bool    isPinned;
    public string? pawnId;
    public string? notes;
}
```

`MemoryEntry.Create()` (Source/Data/MemoryEntry.cs:20-32): factory that auto-generates `id = "mem-{tick}"` and auto-pins `Dark` type entries.

### 2.3 Event Capture via Harmony Patches

Six trigger sources, each a Harmony `Postfix` on a vanilla method:

| Patch | Hook Target | Guards | Pawn Memory | Narrator Sync |
|-------|------------|--------|-------------|---------------|
| `Patch_AddHediff` (Source/Triggers/Patch_AddHediff.cs:8-10) | `Pawn_HealthTracker.AddHediff` | `IsFreeNonSlaveColonist`, `isBad\|\|tendable\|\|makesSickThought` | Yes | `imp >= pawnToNarratorThreshold` (default 0.8) |
| `Patch_PawnKill` (Source/Triggers/Patch_PawnKill.cs:10-11) | `Pawn.Kill` | none (processes all deaths) | All related colonists | Always (imp=1.0) |
| `Patch_MentalBreak` (Source/Triggers/Patch_MentalBreak.cs:9-10) | `MentalStateHandler.TryStartMentalState` | `__result==true`, `IsFreeNonSlaveColonist` | Yes | Always |
| `Patch_SkillLevelUp` (Source/Triggers/Patch_SkillLevelUp.cs:9-10) | `SkillRecord.Learn` | `Level > previousLevel`, `IsFreeNonSlaveColonist` | Yes | `imp >= pawnToNarratorThreshold` |
| `Patch_AddRelation` (Source/Triggers/Patch_AddRelation.cs:8-10) | `Pawn_RelationsTracker.AddDirectRelation` | `IsFreeNonSlaveColonist` | Both pawns | `imp >= pawnToNarratorThreshold` |
| `Patch_IncidentWorker` (Source/Narrator/Patch_IncidentWorker.cs:9-10) | `IncidentWorker.TryExecute` | `__result==true`, `imp >= narratorEventThreshold` (0.2) | No | Always (narrator-only) |

Each trigger: (1) checks `Settings.enableMemory` + per-trigger toggle, (2) estimates importance via a heuristic, (3) builds localized content string via `Translate()`, (4) calls `store.AddActive(MemoryEntry.Create(...), ...)`, (5) optionally syncs to narrator.

### 2.4 Work Session Aggregation

```csharp
// Source/Aggregation/WorkSessionAggregator.cs:11-12
public class WorkSessionAggregator : GameComponent { ... }
```

Triggered by `Patch_StartJob_Memory` (Source/Aggregation/Patch_StartJob_Memory.cs:9-10) on `Pawn_JobTracker.StartJob`. Complex three-category routing:

- **Blacklisted jobs** (Wait, Goto, idle variants): ignored unless in `SignificantJobs` (Attack, Rescue, Tend, etc.) — those recorded as single events
- **Whitelisted jobs** (Sow, Harvest, Mine, Build, Research, Haul, etc.): aggregated into sessions
- **Everything else**: triggers idle-gap detection

Session logic (`OnJobStarted`, Source/Aggregation/WorkSessionAggregator.cs:25-82):
```
Same jobDef + same pawn → increment count, track totalTicks
Job change OR timeout (2500 ticks) → FlushSession
Flush: if count >= minAggregationCount → create single MemoryEntry
       content = "Session: {jobLabel} x{count} ~{hours}h"
```

Idle gaps > `idleGapThresholdTicks` (default 6000 = 2.4h) record an idle entry.

### 2.5 Importance Estimation Heuristics

Hardcoded per-trigger: Injury (lethal→0.9, chronic→0.8, tendable→0.7, sickThought→0.6, else→0.5); Death (related→1.0, unrelated→0.85); MentalBreak (Berserk→0.95, Serious→0.8, else→0.7); SkillUp (L15+→0.7, else→0.5); Relation (Spouse→0.95, Parent→0.9, Sibling→0.85, else→0.6); Incident (Death→1.0, Raid→0.9, Disease→0.75, Trader→0.6, else→0.3). Work session: >6h→0.5, else→0.4. Significant jobs: Attack→0.9, Rescue→0.8, Tend→0.8, Tame→0.7.

### 2.6 Prompt Injection via Provider Pattern

```csharp
// Source/Injection/MemoryContextProvider.cs:13-16
public static void Register() {
    RimMindAPI.RegisterPawnContextProvider("memory_pawn", pawn => {
        // ... build context string from active, archive, dark
    }, PromptSection.PriorityMemory);

    RimMindAPI.RegisterStaticProvider("memory_narrator", () => {
        // ... build narrator context string
    }, PromptSection.PriorityAuxiliary);
}
```

**Selection logic** (Source/Injection/MemoryContextProvider.cs:26-48):
- From active: `Take( (int)(maxActive * activeInjectRatio) )` — newest-first, no relevance filter
- From archive: `Take( (int)(maxArchive * archiveInjectRatio) )` — highest-importance-first
- Dark: all entries, always injected
- Each entry formatted: `"- {timeAgo}: {content}"`
- **No semantic search, no relevance ranking, no token budgeting, no tier differentiation.**

### 2.7 Time Formatting

```csharp
// Source/Core/TimeFormatter.cs:11-33
public static string FormatTimeAgo(int eventTick, int nowTick) {
    int delta = nowTick - eventTick;
    if (delta < 1h)        → "Just now"
    if (delta < 6h)        → "{N} hours ago"
    if (delta < 1 day)     → "Today"
    if (delta <= 3 days)   → "{N} days ago"
    else                   → "Day {N}, {H}:00" (absolute game date)
}
```

### 2.8 Dark Memory (LLM Compression)

```csharp
// Source/DarkMemory/DarkMemoryUpdater.cs:14-18
public class DarkMemoryUpdater : GameComponent {
    private const int DailyInterval = 60000; // once per game day
    private const int JitterRange = 3000;    // stagger pawn updates
```

**Flow** (Source/DarkMemory/DarkMemoryUpdater.cs:78-119):
1. Every daily tick: collect today's active entries + existing dark entries
2. Build prompt: today's events + existing impressions + "merge into {N} summary sentences"
3. `RimMindAPI.RequestAsync()` → low priority, expires after `requestExpireTicks`
4. Response: JSON `{ "dark": ["summary1", "summary2", ...] }`
5. Replace `store.dark` entirely — old impressions overwritten
6. Dark entries always `isPinned=true`, importance=1.0, type=Dark

Per-pawn: 3 dark entries (default). Narrator: 10 dark entries (default).

### 2.9 Decay & Pruning

```csharp
// Source/Core/ImportanceDecayCalculator.cs:7-16
public static float Decay(float importance, float rate) => importance * (1f - rate);
public static bool ShouldRemove(float importance, float threshold) => importance < threshold;
```

Applied in `ImportanceDecayManager.ApplyDecay()` (Source/Decay/ImportanceDecayManager.cs:9-23):
- Decays active and archive lists (skips pinned)
- Removes archive entries below threshold (skips pinned)
- Default: `decayRate=0.02` (2%/day), `minThreshold=0.05`, **disabled by default**

### 2.10 Persistence

```csharp
// Source/Data/RimMindMemoryWorldComponent.cs:7-14
public class RimMindMemoryWorldComponent : WorldComponent {
    private Dictionary<int, PawnMemoryStore> _pawnStores;
    private NarratorMemoryStore _narratorStore;
```

Serialization chain:
```
WorldComponent.ExposeData()
  → Scribe_Collections: Dictionary<int, PawnMemoryStore> (LookMode.Value, LookMode.Deep)
  → Scribe_Deep: NarratorMemoryStore
    → PawnMemoryStore.ExposeData()
      → Scribe_Collections Deep: active, archive, dark
        → MemoryEntry.ExposeData()
          → Scribe_Values: id, content, type, tick, importance, isPinned, pawnId, notes
```

Plus `DarkMemoryUpdater.ExposeData()` persists `_pawnJitter` dict. `WorkSessionAggregator.LoadedGame()` clears transient sessions.

---

## 3. Direct Answers to Gap Questions

**What events/facts captured?** (1) Work sessions — aggregated whitelisted jobs (plant, mine, build, research, haul) with count+duration; idle gaps recorded. (2) Significant single jobs — attack, rescue, tend, recruit (recorded individually). (3) Injuries/disease — bad/tendable/sickThought hediffs, with attacker context. (4) Death — stored for all related colonists + narrator always. (5) Mental breaks — type-specific importance (Berserk 0.95). (6) Skill level-ups — higher weight at L15+. (7) Relationship changes — stored for both pawns. (8) Narrator incidents — raid, siege, disease, trader (filtered by threshold ≥0.2).

**Storage format?** `MemoryEntry`: `id`(string), `content`(localized NL text), `type`(enum: Work/Event/Manual/Dark), `tick`(int), `importance`(float 0–1), `isPinned`(bool), `pawnId`(string?), `notes`(string?). Three `List<MemoryEntry>` per store: active (newest-first, capped), archive (importance-sorted desc, capped), dark (LLM-generated permanent). Persisted via `WorldComponent` keyed by `pawn.thingIDNumber`. **No embeddings, no vector fields, no content-vs-summary split.**

**How selected for prompts?** Purely recency-based with ratio caps: `maxActive × activeInjectRatio` (default 15/30) + `maxArchive × archiveInjectRatio` (default 25/50) + all dark. Each rendered as `"- {timeAgo}: {content}"`. Sections: Recent/Archive/Dark. Registered as context providers consumed by all RimMind modules. **No relevance search, no semantic matching, no token budgeting.**

**Decay/pruning?** Linear: `imp × (1 − rate)`, default 2%/day, **disabled by default**. Applied daily per pawn + narrator in `DarkMemoryUpdater.GameComponentTick()`. Below `minThreshold`(0.05) → removed from archive. Pinned entries immune. Capacity eviction: active overflow demotes oldest non-pinned → archive; archive overflow drops lowest-importance non-pinned. No episode compression at scale.

**Persistence?** Full round-trip via `WorldComponent.ExposeData()`. All `MemoryEntry` fields + dark entries + jitter seeds scribed. `WorkSessionAggregator` sessions NOT persisted (cleared on load). Settings via `ModSettings.ExposeData()`.

---

## 4. ADOPT vs AVOID

### ADOPT

1. **Three-tier storage** — active/archive/dark maps well to our working-window/episodic-log/semantic-store design.
2. **Work session aggregation** — job→session→flush pattern prevents spam; apply to repetitive `PerceptionEvent` sequences.
3. **Pinned/immune entries** — `isPinned` flag excludes from decay+eviction; useful for "strategic goals" persistence.
4. **Provider pattern for injection** — decouples memory from LLM call sites; direct analog to `IWorkingMemory.AssembleAsync()`.
5. **Importance heuristics** — per-event-type hardcoded scores to seed `Episode.Importance` before LLM criticality.
6. **Configurable injection ratios** — our `CognitionTier` could parameterize these instead of hardcoded counts.
7. **Dark memory / LLM compression** — daily summarization into persistent impressions; richer than our template-only legacy compression.
8. **Deterministic jitter** — `new Random(thingID ^ const)` staggers scheduled work; good for embedding batch scheduling.

### AVOID

1. **No semantic retrieval** — recency-only injection. We NEED vector search ("how did we handle the last raid?").
2. **No token budgeting** — ratio×capacity, no awareness of prompt budget. Our `TrimToBudget(90%)` is essential.
3. **No tier-differentiated context** — same memory for micro-decision vs. strategic plan. Our `CognitionTier` is core.
4. **No template engine** — raw translated strings, no structured-event→NL separation. Our `SummaryTemplateEngine` is better.
5. **Dark memory is destructive** — entire `store.dark` replaced each update, losing old impressions. Our append-only log is safer.
6. **No SequenceId** — `id = "mem-{tick}"` has collision risk. Our monotonically increasing `SequenceId` is robust.
7. **Decay disabled + too simple** — linear, off by default. Worth considering if we want any decay model.
8. **Work sessions not persisted** — save/load mid-session loses in-progress aggregation state.
9. **Tight coupling to RimMind-Core** — our Memory layer should only depend on `Framework` + `Perception`.

