# RimMind Context System — Source Analysis

**Source:** [RimWorld-RimMind-Mod-Core](https://github.com/RimWorld-RimMind-Mod/RimWorld-RimMind-Mod-Core)
**Commit:** `d1b2d95` | **License:** MIT (code adoption allowed with attribution) | **Language:** C# (RimWorld mod)

---

## 1. Architecture Summary

RimMind builds game-state context for LLM prompts via a layered pipeline:

```
AICoreAPI (public entry)
  └─► GameContextBuilder (static, fetches Verse/RimWorld data using ContextSettings as filter)
       ├─ BuildMapContext(map, brief)       → map-level text (~100–300 tok)
       ├─ BuildPawnContext(pawn)            → per-pawn text with all filter-controlled fields
       ├─ BuildPawnCompact(pawn)            → one-liner for listing
       └─ BuildSurroundings(pawn)           → room/temperature info
  └─► ContextComposer                       → merges + reorders PromptSection list
       └─► PromptBudget.Compose(sections)   → trims to fit AvailableForInput
             TotalBudget: 4000, ReserveForOutput: 800, AvailableForInput: 3200 tok
```

Async requests flow through `AIRequestQueue` (a `GameComponent`, ticks every frame):
```
AICoreAPI.RequestAsync(req, cb)
  → AIRequestQueue.Enqueue(req, cb, client)
    → per-ModId queue, priority-insert
    → ProcessAllQueues() every 60 ticks
      → cooldown-gated, maxConcurrent=3, local serial
      → FireRequest() on Task.Run (background thread)
        → client.SendAsync(req) → HTTP POST
        → on transient error: re-queue (retry)
        → on success: callback invoked on main thread
```

Provider model: `IAIClient` interface → `OpenAIClient` (UnityWebRequest) or `Player2Client` (local app).

---

## 2. Key Patterns

### 2.1 Context Filter — 29 boolean toggles + presets

`ContextSettings.cs:13-44` — every toggle is a `public bool` defaulting `true`, persisted via RimWorld `Scribe_Values.Look`:

**Pawn info (21 toggles):**
```csharp
// ContextSettings.cs:15-35
public bool IncludeRace = true, IncludeAge = true, IncludeGender = true;
public bool IncludeBackstory = true, IncludeIdeology = true;
public bool IncludeTraits = true, IncludeSkills = true;
public int  MinSkillLevel = 4;  // threshold, not bool
public bool IncludeHealth = true, IncludeCapacities = true;
public bool IncludeMood = true, IncludeMoodThoughts = true;
public bool IncludeCurrentJob = true, IncludeWorkPriorities = true;
public bool IncludeEquipment = true, IncludeInventory = true;
public bool IncludeLocation = true, IncludeRelations = true;
public bool IncludeGenes = true, IncludeSurroundings = true;
public bool IncludeCombatStatus = true;
```

**Map/Environment (8 toggles):**
```csharp
// ContextSettings.cs:38-45
public bool IncludeGameTime = true, IncludeColonistCount = true;
public bool IncludeColonistNames = true, IncludeWealth = true;
public bool IncludeFood = true, IncludeSeason = true;
public bool IncludeWeather = true, IncludeThreats = true;
```

**Provider allow/block lists:**
```csharp
// ContextSettings.cs:47-48
public HashSet<string> disabledProviders = new();
public HashSet<string> exposedProviders = new();   // if non-empty, ONLY these pass
```

**Presets (`ContextSettings.cs:65-121`) — `ContextPreset { Minimal, Standard, Full, Custom }`:**

Minimal (8 on, 21 off): Race, Health, Mood, CombatStatus, ColonistCount, Weather, Threats + MinSkillLevel=4.
Standard (22 on, 7 off): adds Age, Gender, Backstory, Traits, Skills, Capacities, CurrentJob, WorkPriorities, Equipment, Relations, Genes, GameTime, Names, Food, Season. Omits: Ideology, MoodThoughts, Inventory, Location, Surroundings, Wealth.
Full (29 on, 0 off): all toggles true, MinSkillLevel=1. `Custom` means user tweaked individual booleans — preset enum tracks origin.

### 2.2 GameContextBuilder — text context from game state

`GameContextBuilder.cs` — static class, all methods on main thread. Reads `RimMindCoreMod.Settings.Context` to gate every output section. Key pattern:

```csharp
// GameContextBuilder.cs:26 — BuildMapContext entry
public static string BuildMapContext(Map map, bool brief = false)
{
    var ctx = RimMindCoreMod.Settings.Context;
    var sb = new StringBuilder();

    if (ctx.IncludeGameTime) {
        long ticks = Find.TickManager.TicksAbs;
        // ... format date + hour via GenDate
        sb.AppendLine("RimMind.Core.Prompt.TimeFormat".Translate(dateStr, $"{hour:D2}"));
    }
    if (ctx.IncludeColonistCount) {
        var colonists = map.mapPawns.FreeColonistsSpawned;
        // ... list names if IncludeColonistNames
    }
    // ... each section gated by its ctx.Include* bool
}
```

Per-pawn compact format (`GameContextBuilder.cs:198`):
```csharp
// "Name  Age20Y Male Human"
// Mood:Content  Job:Hauling  Room:Indoors(21°C)
```

Uses RimWorld `Translate()` for localization — all section headers are translation keys.

### 2.3 PromptBudget — section-based token budgeting

`PromptBudget.cs` — Compose algorithm:

```csharp
// PromptBudget.cs:17-18
public int TotalBudget { get; set; } = 4000;
public int ReserveForOutput { get; set; } = 800;
public int AvailableForInput => TotalBudget - ReserveForOutput; // 3200

public List<PromptSection> Compose(List<PromptSection> sections)
{
    // 1. Sum estimated tokens; if <= AvailableForInput → return reordered
    // 2. Try compression first (sections with Compress delegate), highest priority first
    // 3. If still over budget: trim lowest-priority sections (highest numeric Priority value)
    //    → PriorityCore=0 NEVER trimmed
    return ContextComposer.Reorder(result); // ascending priority
}
```

Token estimator (`PromptSection.cs:39-47`):
```csharp
public static int EstimateTokens(string text)
{
    // Non-CJK: chars/4  |  CJK: chars/1.5
    // CJK range: U+4E00-9FFF, U+3040-30FF, U+AC00-D7AF
    return (int)Math.Ceiling((charCount - cjk) / 4.0 + cjk / 1.5);
}
```

**Priority constants** (lower = more important): `Core=0, CurrentInput=1, KeyState=3, Memory=5, Auxiliary=8, Custom=10`. `IsTrimable = Priority > PriorityCore` (i.e., Priority 1+ can be trimmed; Core=0 is sacred).

### 2.4 Async Request Queue — retry, 429, serial mode

`AIRequestQueue.cs` — `GameComponent` subclass, ticks every frame.

**Enqueue (`AIRequestQueue.cs:114-152`):**
```csharp
// MaxAttempts: per-request override (-1 sentinel → use settings default)
MaxAttempts = request.MaxRetryCount >= 0
    ? request.MaxRetryCount + 1
    : RimMindCoreMod.Settings.maxRetryCount + 1;  // default: 2+1=3
// Priority insertion (lower numeric = served first)
int insertIdx = queue.FindIndex(t => t.Request.Priority > request.Priority);
```

**Dequeue/Process (`AIRequestQueue.cs:195-267`):** Every 60 ticks (`QueueProcessInterval=60`), `ProcessAllQueues()`:
1. Skips paused queues, cooldown-gated mods
2. Expires stale requests (`ExpireAtTicks > 0 && now > ExpireAtTicks`)
3. Sorts ready requests by priority → enqueue time
4. Fires up to `maxConcurrentRequests=3`, skipping local-model-busy
5. Sets per-mod cooldown after dispatch

**Local model serialization:** `_isProcessingLocalRequest` flag — only one local request processes at a time. `EnqueueImmediate` auto-demotes to `Enqueue` if local endpoint is busy.

**Retry (`AIRequestQueue.cs:319-354`):**
```csharp
bool shouldRetry = !response.Success
    && tracked.AttemptCount < tracked.MaxAttempts
    && IsTransientError(response.Error);
// On retry: re-queues at priority position, increments AttemptCount
// NO exponential backoff — cooldown is the pacing mechanism
```

**IsTransientError (`AIRequestQueue.cs:397-408`):**
```csharp
// Substring match, case-insensitive:
"timeout" | "connection" | "network" | "503" | "502" | "429" | "rate limit"
```

**Timeout (`AIRequestQueue.cs:357-395`):** `timeoutTicks = requestTimeoutMs / 16` (120000ms → 7500 ticks). Active requests exceeding this get `AIResponse.Failure` with `State=Error` and callback invoked.

**Cooldown (`AIRequestQueue.cs:458-467`):**
```csharp
private int GetModCooldownTicks(string modId)
{
    var getter = RimMindAPI.GetModCooldownGetter(modId);
    if (getter != null) { try { return getter(); } catch {} }
    return 3600; // default: 3600 ticks (~1.44 game-hours)
}
```

**HTTP layer (`OpenAIClient.cs`):**
```csharp
// PostAsync: UnityWebRequest, busy-wait polling at 0.1s intervals
float connectTimeout = isLocal ? 300f : 60f;  // seconds
float readTimeout = 60f;                       // seconds
// On ConnectionError/ProtocolError: throws AIHttpException(statusCode, detail)
```

**Settings (`AICoreSettings.cs`):**
```csharp
public int maxConcurrentRequests = 3;
public int maxRetryCount = 2;          // → MaxAttempts = 3 (1 initial + 2 retry)
public int requestTimeoutMs = 120000;  // 2 minutes
```

---

## 3. Direct Answers to Gap Questions

**Q: How does RimMind's context builder compare to our ColonySnapshot DTO?**
RimMind builds **text** (localized, human-readable, unstructured) — no typed DTOs, no JSON schema, no ID contracts. Our `ColonySnapshot` is a **structured JSON DTO** with entity IDs (`building_id`, `zone_id`, etc.), typed enums, and omit-null serialization. RimMind's approach is simpler but the LLM gets no machine-addressable entity references — it outputs natural language that mod-specific parsers must interpret. Our structured approach is strictly superior for tool-calling because IDs survive the prompt→response round-trip.

**Q: How does their token budgeting compare to ours?**
RimMind: manual char/4 + CJK estimation, priority-based trimming with Core=0 protection, optional compression delegates. Budget: 4000 total / 800 output reserve / 3200 input. Ours (per G-02): ~5K token budget, ceiling 5,500. RimMind's section-based trim is a **strong pattern to adopt** — our `ColonySnapshot` currently has no runtime trim logic; we'd prune entity lists (Buildings beyond 10, Bills beyond 10) as spec'd in the trim order but have no priority system protecting critical sections from accidental trim.

**Q: What entity IDs does RimMind expose vs. what G-02 requires?**
RimMind exposes **none** — it's text-only context. No `production_table_id`, `bill_id`, `zone_id`, `item_id`, `settlement_id`, `outfit_name`, `drug_policy_name`, or `food_restriction_name`. Our G-02 additions (Buildings, Zones, Bills, NotableItems, Policies, Factions) are missing entirely from RimMind's context builder. This confirms G-02 is unique value-add for our project.

**Q: How does RimMind handle rate limiting (429) and retry?**
- **Detection:** substring match on "429" or "rate limit" in error text (no Retry-After header parsing)
- **Action:** re-queue at priority position, increment AttemptCount — **NO exponential backoff, no jitter**
- **Pacing:** per-mod cooldown (3600 ticks default ≈ 1.44 game-hours) prevents spam, but doesn't adapt to 429
- **Max attempts:** 3 total (1 initial + 2 retries), configurable per-request or globally
- **Gap:** no Retry-After header parsing, no backoff growth, no circuit breaker

**Q: How does serial mode for local models work?**
- `OpenAIClient.IsLocalEndpoint` detects loopback via `Uri.IsLoopback || "localhost" || "host.docker.internal"`
- `_isProcessingLocalRequest` boolean: if `true`, all incoming requests (even `EnqueueImmediate`) demote to `Enqueue`
- Local endpoint also gets longer connect timeout (300s vs 60s) and no retry for `EnqueueImmediate` (MaxAttempts=1)
- Local model requests still count toward `maxConcurrentRequests` cap

---

## 4. ADOPT vs AVOID

### ADOPT

1. **Priority-protected token trim** — `PromptSection.PriorityCore=0` untrimmable, numeric tiers. Add to our `ColonySnapshot` serializer: mark entity lists with priority, trim low-priority first when over ceiling.
2. **-1 sentinel for per-request MaxRetryCount** — `-1` means "use global default." No nullable/enum needed. Clean.
3. **Mod/Module cooldown registry** — `GetModCooldownTicks` delegates to registered getters per mod. Our orchestration tier cooldowns could use same pattern.
4. **Local endpoint auto-detection** — `IsLoopbackEndpoint` checks `Uri.IsLoopback`, `localhost`, `host.docker.internal`. Covers Ollama/Docker/LM Studio.
5. **ExpireAtTicks on requests** — stale requests auto-skipped. Critical for game-time-sensitive actions.
6. **Provider registration** (`RimMindAPI` static/pawn/dynamic providers) — mods register data providers, Core collects. Simpler than our `IEntityResolver` (Func delegates vs interfaces).
7. **ConcurrentQueue callback marshaling** — `_pendingFireResults/_results/_pendingLogs` drained on main thread in `GameComponentTick`. Thread-safe without locks. Use for our `EventBus`.

### AVOID

1. **No entity IDs in context** — text-only, LLM can't reference game objects. Downstream string parsing required. Our structured DTO + ID contract is strictly better for tool-calling.
2. **No Retry-After header parsing** — substring match on "429" ignores `Retry-After` seconds. Unnecessary retries during rate-limit windows.
3. **No exponential backoff** — retry re-queues immediately. Fixed cooldown (3600 ticks) doesn't adapt to rate-limit state.
4. **Busy-wait HTTP polling** — `PostAsync` polls `webRequest.downloadedBytes` every 0.1s. Blocks background thread. Use async `SendWebRequest()`.
5. **Heuristic token estimation** — char/4 approximation, never calibrated against actual `usage.total_tokens` from API responses.
6. **No circuit breaker** — continuous transient errors re-queue until MaxAttempts exhausted. No escalation to hard pause.
7. **Immediate requests: MaxAttempts=1, no retry** — `EnqueueImmediate` gets no second chance on transient failure. Intentional but undocumented.
8. **Text-only context: no schema validation** — no field-level constraints, no consistency guarantee across calls.
