# RimMind Actions — Source Analysis for RimAI.Framework

**Source repo:** RimWorld-RimMind-Mod-Actions · **Version:** v0.0.1
**License:** MIT (code adoption allowed with attribution)
**Author:** mcocdaa · **RimWorld:** 1.6 · **.NET:** net48

---

## 1. Architecture Summary

RimMind-Actions is a thin adapter between AI-generated string intents and
RimWorld game APIs. It is NOT an LLM-facing action system — the caller
(Advisor mod) selects actions and resolves Pawns BEFORE dispatching.

- **Entry:** Static `RimMindActionsAPI` — `RegisterAction`, `Execute`, `ExecuteBatch`.
- **Abstraction:** `IActionRule` interface per intent, registered by string `intentId`.
- **Main-thread safety:** `DelayedActionQueue` (GameComponent) defers execution
  by 1.5s ±20% jitter, so off-thread callbacks can enqueue safely.
- **Whitelist:** 24 built-in actions registered at mod construction via
  `Dictionary<string, IActionRule>` — no enum, no JSON schema.
- **Harmony:** `PatchAll()` only — no `[HarmonyPatch]` attributes in this assembly.
- **Risk gating:** `RiskLevel` enum (Low→Critical) + player-disablable intents via settings.

---

## 2. Key Patterns

### 2.1 Mod Entry + Harmony Bootstrap

```csharp
// RimMindActionsMod.cs:14-18
public RimMindActionsMod(ModContentPack content) : base(content)
{
    Settings = GetSettings<RimMindActionsSettings>();
    new Harmony("mcocdaa.RimMindActions").PatchAll();
    RegisterBuiltinActions();  // 24 IActionRule registrations
}
```

About.xml declares `brrainz.harmony` as dependency + `loadAfter`.
csproj: `<PackageReference Include="Lib.Harmony.Ref" Version="2.*" PrivateAssets="All" />`.
No custom Harmony patches in this assembly — PatchAll is a cross-mod hook only.

### 2.2 Action Registration + Execution w/ Player Gating

```csharp
// RimMindActionsAPI.cs:26-37, 49-77
private static readonly Dictionary<string, IActionRule> _rules = new();

public static void RegisterAction(string intentId, IActionRule rule)
    => _rules[intentId] = rule;

public static bool Execute(string intentId, Pawn actor, Pawn? target=null,
                           string? param=null, bool requestQueueing=false)
{
    if (!_rules.TryGetValue(intentId, out var rule))
    { Log.Warning($"Unknown intentId: {intentId}"); return false; }
    if (!RimMindActionsMod.Settings.IsAllowed(intentId))  // player gating
    { Log.Message($"'{intentId}' disabled by settings"); return false; }
    return rule.Execute(actor, target, param, requestQueueing);
}
```

### 2.3 IActionRule Contract (identity + risk + execution)

```csharp
// IActionRule.cs:8-42
public interface IActionRule
{
    string IntentId    { get; }     // e.g. "force_rest"
    string DisplayName { get; }     // Translate() key
    RiskLevel RiskLevel { get; }    // Low | Medium | High | Critical
    bool IsJobBased => false;       // true → uses TryTakeOrderedJob
    bool Execute(Pawn actor, Pawn? target, string? param, bool requestQueueing);
}
```

`IsJobBased` controls batch behavior: first job action for a pawn clears the
queue (`requestQueueing=false`), subsequent actions append (`true`).

### 2.4 Risk-Tier Gating

```csharp
// RiskLevel.cs:3-9 — four tiers
Low      // routine, no side effects (move_to, cancel_job)
Medium   // mild side effects (social_relax, add_thought)
High     // major behavior change (recruit_agree, arrest_pawn)
Critical // irreversible / global (trigger_mental_state, trigger_incident)
```

```csharp
// RimMindActionsMod.cs:44-49 — settings UI red-boxes High+
if (risk >= RiskLevel.High)
    Widgets.DrawBoxSolidWithOutline(listing.GetRect(0f),
        new Color(0.3f, 0f, 0f, 0.15f), Color.clear);
```

Player can disable individual intents via `HashSet<string> DisabledIntents`
persisted through `Scribe_Collections` (RimMindActionsSettings.cs:23-30).

### 2.5 Main-Thread Scheduler (DelayedActionQueue)

```csharp
// DelayedActionQueue.cs:85-117 — GameComponent.Tick at 60 Hz
float dt = 1f / 60f;
for (int i = _queue.Count - 1; i >= 0; i--)
{
    var p = _queue[i];
    if (p.IsCancelled || p.Actor?.Dead == true || p.Actor?.Destroyed == true)
    { _queue.RemoveAt(i); continue; }
    p.TimeRemaining -= dt;
    if (p.TimeRemaining > 0f) continue;
    try { RimMindActionsAPI.Execute(p.IntentId, p.Actor, p.Target, p.Param); }
    catch (Exception e) { Log.Error($"execute '{p.IntentId}' failed: {e}"); }
    _queue.RemoveAt(i);
}
```

Enqueue with default 1.5s delay ±20% jitter (`DelayedActionQueue.cs:34-54`).
Queue NOT persisted (`ExposeData` is empty at line 119-123).

### 2.6 Target Resolution — NO String IDs

**Critical finding:** The Actions layer has zero entity resolution. The caller
(Advisor mod) resolves `Pawn` objects BEFORE calling `Execute`. This is a
fundamentally different architecture from our LLM→DTO→Action pipeline.

```csharp
// BatchActionIntent.cs:12-19 — caller pre-resolves Pawns
public class BatchActionIntent
{
    public Pawn  Actor  = null!;   // RESOLVED by caller
    public Pawn? Target;           // RESOLVED by caller
    public string? Param;          // unstructured string only
}
```

Work targets use **spatial/coordinate resolution**, not entity IDs:
`"Mining@45,32"` → `actor.Map.thingGrid.ThingsListAt(cell)` →
`WorkGiver_Scanner.HasJobOnThing(cell)` (`PawnActions.cs:150-189`).

### 2.7 Parameter Validation — Ad-Hoc Per Action

No centralized schema. Each action parses its own `string? param`:

```csharp
// MoveToAction: comma coords — PawnActions.cs:458-475
var parts = param.Split(',');  int x=Parse(parts[0]), z=Parse(parts[1]);

// SetWorkPriorityAction: "WorkType,priority" — PawnActions.cs:596-607
var parts = param.Split(',');  priority=Clamp(int Parse(parts[1]),0,4);

// EatFoodAction: keyword fuzzy-match — PawnActions.cs:692-697
t.def.defName.ToLowerInvariant().Contains(kw)
    || t.LabelShort.ToLowerInvariant().Contains(kw);
```

### 2.8 Failure Reporting — bool + Log, No Structure

```csharp
// All Execute methods return bool. Error detail → Log.Warning/Error only.
public static int ExecuteBatch(...)
{
    int successCount = 0;
    foreach (var intent in intents)
        if (rule.Execute(...)) successCount++;
    return successCount;  // caller gets integer count, no per-command details
}
```

### 2.9 Dev Tooling (DebugAction Harness)

`ActionsDebugActions.cs` — 30+ `[DebugAction]` methods on a
`[StaticConstructorOnStartup]` class:
- `ShowRegisteredIntents()` — dumps all registered actions + risk levels
- `TestForceRest()`, `TestDraft()`, … — one per action, tests via selected pawn
- `ShowDelayedQueue()` — inspects pending queue items
- `TestBatchJobSequence()` — 5-step batch sequence test
- `GetWorkTargets(pawn, "Mining", 8)` — enumerates prompt candidates
All invoke through `RimMindActionsAPI.Execute` — same code path as AI.

### 2.10 Localization

All display strings use `Translate()` keys. Bilingual: English + Chinese
Simplified (`Languages/English/Keyed/RimMind_Actions.xml`, 104 keys).
Includes risk tooltip translations for all 4 tiers.

---

## 3. Direct Answers to Gap Questions

### G-03: EntityId / Target Resolution

| Question | RimMind's Answer |
|---|---|
| How does the LLM name a pawn/thing? | **The LLM does not name targets.** The Advisor mod selects actions and resolves Pawns before calling the Actions API. |
| How are names resolved to game objects? | No resolution in Actions layer. Work targets use `"WorkType@x,z"` coordinate notation → spatial lookup via `thingGrid.ThingsListAt(cell)`. |
| What is their ID system? | None. Caller responsibility entirely. |

**For RimAI:** We MUST build `IEntityResolver` (G-03). RimMind avoids this
problem by pushing resolution upstream. Our architecture (LLM generates DTOs
with string entity references) requires string→object resolution inside Action.

### G-13: Action Whitelist / Schema

| Question | RimMind's Answer |
|---|---|
| How is the whitelist defined? | `Dictionary<string, IActionRule>` populated at mod construction. Any mod can call `RegisterAction`. |
| Schema/parameter definition? | None. Each `Execute` manually parses `string? param`. |
| Unregistered types? | `Log.Warning` + return `false`. Never throws. |

**For RimAI:** Adopt the `Dictionary<string, ICommandHandler>` registration pattern
(extensible, type-safe). REJECT the unstructured `string? param` — we need typed
parameter DTOs validated against per-type schemas.

### G-21: Zone/Area Parameters

RimMind has NO zone commands. All spatial targeting uses `"x,z"` coordinate
notation. This confirms our G-21 resolution (rect `x,z,w,h` for zone creation)
is an improvement, not following RimMind.

### G-28: Dev Tooling / Command-File Harness

RimMind's `[DebugAction]` pattern is directly applicable: 30+ testable debug
actions, each calling through the real API. Perfect model for our M2 `RimAI:
Run command file` and `RimAI: Dump snapshot` debug actions.

---

## 4. ADOPT vs AVOID

### ADOPT

| Pattern | Source (file:line) | Why |
|---|---|---|
| `IActionRule` interface w/ `RiskLevel` | `IActionRule.cs:8-42` | Clean identity+risk+exec contract. Our `ICommandHandler` should mirror. |
| `Dictionary<string, IActionRule>` registration | `RimMindActionsAPI.cs:26` | Simple, extensible whitelist. Beats enum maintenance. |
| Player-disablable intents via `ModSettings` | `RimMindActionsSettings.cs:15-21` | Risk-tier gating player can control per-action. |
| `DelayedActionQueue` as `GameComponent` | `DelayedActionQueue.cs:11-117` | Thread-safe main-thread scheduling w/ time delay + jitter. |
| `[DebugAction]` per action, `[StaticConstructorOnStartup]` | `ActionsDebugActions.cs:16-25` | Dev harness pattern for M2 command-file playback (G-28). |
| `ExecuteBatch` w/ per-pawn `requestQueueing` tracking | `RimMindActionsAPI.cs:92-123` | Multi-step: step 1 interrupts, rest queues on same pawn. |
| `Harmony.PatchAll()` + `brrainz.harmony` modDep | `About.xml:13` | Reference for M1 Harmony setup (G-05). |
| `Translate()` keys for ALL display strings | `Languages/.../RimMind_Actions.xml` | Bilingual-ready. Our G-27 prep should do the same. |

### AVOID

| Issue | Why avoid |
|---|---|
| **No structured `CommandResult`** — returns `bool` | Can't tell LLM WHY action failed. We need our `CommandResult` w/ categories. |
| **Unstructured `string? param`** — per-action manual parsing | No schema validation. LLMs need typed DTOs. |
| **No string-based entity resolution** — caller pre-resolves `Pawn` | Our LLM generates string IDs from Perception. We need `IEntityResolver`. |
| **No staleness guard** — no `SnapshotTick` on queued commands | Stale-state execution risk. Our 15k-tick guard (G-17) is essential. |
| **Queue not persisted** — `ExposeData` is empty | Our G-16 resolution (persist queue) is needed for session continuity. |
| **No circuit breaker** — single `catch(Exception)` in tick | Our G-18 breaker (5 failures → pause) is needed for robustness. |
