# 06 — Plan Audit & Expansion (Gap Analysis)

**Status:** authoritative supplement. Read AFTER 00-MASTER-PLAN and your layer spec. Where this doc conflicts with 00–05, THIS DOC WINS (it is the reconciliation pass).
**Produced by:** 2026-07-05 audit session (second pass over the Sprint-2 plan package).
**Verified against code:** `RimAI.Framework.Contracts/Models/*.cs`, `RimAI.Framework/Source/API/RimAIApi.cs`, `RimAI.Agent/*` skeleton.

Each gap has an ID (`G-xx`), severity (**BLOCKER** = milestone cannot exit without it; **MAJOR** = wrong/contradictory spec; **MINOR** = quality/polish), the resolution, and the milestone it attaches to.

---

## 1. BLOCKER gaps

### G-01 — Framework returns no token usage; BudgetManager cannot function
`UnifiedChatResponse` (Contracts/Models/UnifiedChatModels.cs) exposes `FinishReason` but **no usage fields**. 05 §4 requires `Deduct(tier, actualTokens)` with actual tokens. Verified by grep: zero `Usage`/`tokens` members in Contracts.
**Resolution — new milestone M0.5 (Framework API gaps), before M3:**
1. Add `UsageInfo { PromptTokens, CompletionTokens, TotalTokens }` to `UnifiedChatResponse` and `UnifiedEmbeddingResponse`; populate in response translators for all provider templates (OpenAI-compat `usage`, Anthropic `usage`, Gemini `usageMetadata`). Null-safe: providers that omit usage → `UsageInfo=null`.
2. Budget fallback when `UsageInfo==null`: estimate = `ceil(promptChars/4) + ceil(completionChars/4)`. `IBudgetManager.Deduct` accepts `(actual ?? estimate)`.
3. Streaming: accumulate usage from final SSE chunk where the provider sends it; else estimate.
**Tracked:** bd issue under epic M0.5.

### G-02 — Perception snapshot lacks the entities the tool catalog references
Tools/commands take `production_table_id`, `bill_id`, `zone_id`, `item_id`, `settlement_id`, `outfit_name`, `drug_policy_name`, `food_restriction_name` — but `ColonySnapshot` (01 §2) contains **none of these**. The LLM cannot emit an ID it never saw. This breaks M2 scripted playback (no IDs to script against) and every LLM tier.
**Resolution — extend `ColonySnapshot` (schema Version bump → 2):**
```csharp
public List<BuildingSnapshot> Buildings { get; init; }   // production/comms/power, top 20 by relevance
public List<ZoneSnapshot> Zones { get; init; }
public List<BillSnapshot> Bills { get; init; }           // all active bills, cap 20
public List<ItemSnapshot> NotableItems { get; init; }    // weapons/apparel/medicine on map, cap 15
public PolicySnapshot Policies { get; init; }            // available outfit/drug/food policy names
public List<FactionSnapshot> Factions { get; init; }     // name, goodwill, canTrade, settlementIds (cap 8)
```
```csharp
public class BuildingSnapshot { string Id, DefName, Kind; /* Kind: "Production"|"Comms"|"Power"|"Bed"|"Storage" */ int X, Z; bool Usable; }
public class ZoneSnapshot     { string Id, ZoneType, Label; int CellCount; string PlantDefName; /* growing zones */ }
public class BillSnapshot     { string Id, TableId, RecipeDefName, RepeatMode; int TargetCount; bool Suspended; }
public class ItemSnapshot     { string Id, DefName, Quality; int StackCount, X, Z; }
public class PolicySnapshot   { List<string> OutfitNames, DrugPolicyNames, FoodRestrictionNames; }
public class FactionSnapshot  { string Name; int Goodwill; bool Hostile, CanTradeNow; }
```
Trim order update (01 §2.5): NotableItems → Factions → Buildings beyond 10 → Bills beyond 10 → (existing chain). Token budget rises to **~5K**; acceptance ceiling 5,500 tokens.

### G-03 — ID contract between Perception and Action is undefined
Nothing specifies how string IDs are minted or resolved. Without one shared rule, M2's resolver and M1's snapshots will disagree.
**Resolution — single rule, new file `RimAI.Agent/Source/Shared/EntityId.cs` (namespace `RimAI.Agent.Shared`):**
- Pawns/Things/Buildings: `Thing.ThingID` (e.g. `Human12345`, `TableMachining54321`). Stable across save/load.
- Zones: `Zone.ID` int → `"zone_" + ID`.
- Bills: `Bill.loadID` int → `"bill_" + loadID`.
- Factions: `Faction.loadID` → `"faction_" + loadID`; settlements: `Settlement.ID` similarly.
- Action layer gets `IEntityResolver` (main-thread only): `TryResolvePawn(string id, Map, out Pawn)` etc. — one lookup helper used by every handler; returns `FailureCategory.EntityNotFound` on miss. Perception uses the same class to mint IDs. Shared code, zero drift.

### G-04 — No construction capability, but M6 promises "landing → stable colony"
`ActionCommandType` has no build/place command. Without construction the AI cannot make a freezer, defenses, or beds — M6 exit is unreachable.
**Resolution — new command family (M4 scope, tools tier Tactical/Strategic):**
| CommandType | Required keys | Notes |
|---|---|---|
| `PlaceBlueprint` | `building_def_name`, `cell_x`, `cell_z`, `rotation(0-3)` | `stuff_def_name` optional; validator checks def in whitelist of ~40 core buildings (walls, doors, beds, tables, coolers, heaters, turrets, batteries, solar, workbenches…) |
| `PlaceFloor` | `terrain_def_name`, `cells` (rect: `x,z,w,h`) | rect max 15×15 |
| `CancelBlueprint` | `blueprint_id` | |
| `Deconstruct` | `target_id` | classified Destructive (guardrail) |
Colonists build via normal Construction work — the AI only places blueprints, which matches player mechanics. Snapshot addition: `ResourceSnapshot` already covers materials.
Also missing, same family of "plays fully" holes — add in M4/M5:
- `SetPlantToGrow` (`zone_id`, `plant_def_name`) — growing zones are useless without it.
- `SetPrisonerInteraction` (`prisoner_id`, `mode`: enum Recruit/Reduce/Release/NoInteraction) — strategic recruitment.
- `AddDesignation` `designation_type` enum is now **defined**: `Hunt, Tame, Slaughter, Mine, CutPlants, Harvest, HaulThing, Strip, Open` (Deconstruct moved to its own Destructive command).

### G-05 — No Harmony reference in RimAI.Agent
Perception spec (01 §4.3) requires 7 Harmony patches; `RimAI.Agent.csproj` has no `Lib.Harmony`, About.xml has no Harmony dependency and no `loadAfter`.
**Resolution (M1, first task):** add `<PackageReference Include="Lib.Harmony" Version="2.*" />` (ExcludeAssets=runtime; ship against the game's bundled Harmony via `brrainz.harmony` mod dependency). About.xml: add `brrainz.harmony` to `modDependencies` and `<loadAfter>` for both `brrainz.harmony` and `kilokio.rimai.framework`.

### G-06 — No test project exists, but every milestone's acceptance criteria demand unit tests
Specs cite `RimAI.Agent.Tests/Fixtures/…`; solution has no such project.
**Resolution (M1, second task):** create `RimAI.Agent.Tests` (net472, xunit, `ProjectReference` → RimAI.Agent; Krafs.Rimworld.Ref gives compile-time refs — tests must only exercise DTO/pure-logic code paths, never `Find.*`). CI gate = `dotnet test`. Fixture folders per 02 §12 / 03 §13.

### G-07 — No LLM client abstraction; Cognition/Memory are untestable against static `RimAIApi`
02 §12 says "mock RimAIApi (via IRimAIApi wrapper interface)" but never defines it.
**Resolution — new file `RimAI.Agent/Source/Shared/ILlmClient.cs`:**
```csharp
public interface ILlmClient {
    Task<Result<UnifiedChatResponse>> ChatWithToolsAsync(List<ChatMessage> msgs, List<ToolDefinition> tools, string conversationId, CancellationToken ct);
    Task<Result<UnifiedEmbeddingResponse>> EmbedAsync(UnifiedEmbeddingRequest req, CancellationToken ct);
}
```
Production impl delegates to `RimAIApi`; tests inject fakes. Cognition and Memory take `ILlmClient` in constructors — they never name `RimAIApi` directly. (04 §1 "only layer that directly calls RimAIApi" is amended to "via ILlmClient".)

---

## 2. MAJOR gaps (contradictions & wrong facts in 00–05)

### G-08 — Tick math error (05 §3)
1 game-hour = 2,500 ticks; 1 game-day = 60,000. Therefore Tactical ~6h = **15,000 ticks** (05 said 60,000 = a full day) and Strategic ~2d = 120,000 (correct). 05 has been corrected in place.

### G-09 — Wrong GameComponent override (05 §2)
`GameComponentUpdate()` runs per frame including while paused; game-time cadence belongs in **`GameComponentTick()`**. Split: Tick() = phases 1–5 (cadence, queue drain); Update() = real-time-only concerns (confirmation-dialog countdown while paused). 05 corrected in place.

### G-10 — Three names for the same enum
`PlanningTier` (02) vs `PlannerTier` (04) vs `CognitionTier` (05). **Canonical: `CognitionTier { Reactive, Tactical, Strategic }` in `RimAI.Agent.Shared`.** 02/04 corrected in place; skeleton rename tracked in bd.

### G-11 — `GameAlert` is referenced (02, 05) but never defined; Perception emits `PerceptionEvent`
**Resolution:** delete the `GameAlert` concept. Signatures become `PlanReactiveAsync(PerceptionEvent trigger, …)`, `ForceTrigger(CognitionTier, PerceptionEvent? trigger)`. Corrected in place.

### G-12 — Tool catalog (02 §6.1: 24 tools) ≠ command whitelist (03 §4: 35 enum values; text says "30")
Unmapped command types: `SuspendBill, ResumeBill, UnassignFromZone, RenameZone, HoldFire, FireAtWill, PrioritizeSurgery, SetMedicalDefaults, DropItem, SetRecreation`.
**Resolution:** every `ActionCommandType` MUST have a 1:1 tool. Add the 10 missing tools (tiers: bills/zones → Tactical; HoldFire/FireAtWill/PrioritizeSurgery → Reactive; SetMedicalDefaults/DropItem/SetRecreation → Tactical) **plus** the G-04 additions (`place_blueprint, place_floor, cancel_blueprint, deconstruct, set_plant_to_grow, set_prisoner_interaction`). Post-audit whitelist = **41 command types**; "30" in 03 corrected to "all enum values".

### G-13 — M2 scope contradiction: master plan says "10 core commands", 03 §11-M2 demands all handlers
**Resolution — phased handler delivery:**
- **M2 core 12:** DraftColonist, UndraftColonist, MoveTo, AttackTarget, SetWorkPriority, AddBill, RemoveBill, CreateZone (growing+stockpile), AddDesignation, RescueColonist, TendPawn, CancelCommand.
- M3: + HoldFire, FireAtWill, CapturePrisoner, EquipItem, PrioritizeSurgery.
- M4: + remaining bills/zones/policies/schedule + construction family (G-04).
- M5: + SetResearchProject, InitiateTrade, FormCaravan, SetPrisonerInteraction.
`CommandDispatcher.Dispatch` on an unregistered type returns `ValidationError("not yet implemented")` — never throws. 00 §5-M2 and 03 §11 corrected in place.

### G-14 — Cognition loop feeds "ok" to the LLM for commands that haven't executed
02 §7.1 sends `toolResultMessage("ok")` at *validation* time; execution happens ticks later on the main thread. The LLM believes actions succeeded during the session.
**Resolution:** tool result string becomes `"accepted (queued for execution; result in next briefing)"` — semantically honest. Execution outcomes reach the next session via `ActionFeedback` → WorkingMemory (03 §9 loop already specifies this). Also fixes the 02 §7.1 message-ordering bug: append the assistant message **before** its tool-result messages (OpenAI wire order). Corrected in place.

### G-15 — Reactive latency killed by cross-tier cooldown (05 §3)
`MinimumSpacingTicks=5000` "against all tiers" means a raid within ~2 game-hours of a tactical fire waits. **Resolution:** Reactive is exempt from cross-tier spacing; it has only its own 600-tick self-cooldown (~15 min game-time) to stop event storms. Tactical/Strategic keep cross-tier spacing. Corrected in place.

### G-16 — Queue persistence contradiction (05 §10 "queued commands preserved" vs 03: queue not in any ExposeData)
**Resolution:** queue is **dropped** on save/load (commands were planned against a snapshot that no longer matches reality). 05 corrected. Rationale: stale commands are worse than a skipped beat; next cadence replans within 6 game-hours.

### G-17 — In-flight session concurrency undefined
Nothing stops Tactical and Strategic overlapping, or two Reactive sessions racing.
**Resolution (05 §2 amendment):** max **one in-flight planning session per tier**; a tier whose session is still running skips its cadence check. Reactive events arriving while a Reactive session is in flight are coalesced: keep highest-criticality event, re-fire when the session returns. Snapshot handed to a planner carries `Tick`; Action layer rejects commands whose source snapshot is older than **15,000 ticks** (staleness guard; new optional `ActionCommand.SnapshotTick`).

### G-18 — No failure containment (hung calls, error storms)
**Resolution (05 §5 amendment — circuit breaker):** per-tier LLM timeout via `CancellationTokenSource` (Reactive 60s, Tactical 120s, Strategic 180s real-time). Circuit breaker: ≥5 consecutive `PlanResult` failures across tiers → AI auto-pauses, letter shown to player ("RimAI paused after repeated errors"), manual resume. Breaker state not persisted (fresh chance on load).

---

## 3. MINOR gaps (quality / polish — file as bd chores, not blocking)

- **G-19 Advisor-mode surface:** "suggest only" has no UI. v1: suggestions land in the Decision Log flagged `Suggested` (new bool on `DecisionLogEntry`) + a RimWorld letter per Strategic suggestion batch. Full suggestion-inbox UI deferred to M6.
- **G-20 Trade/caravan realism:** `InitiateTrade` cannot click a dialog. v1 semantics: tool gains `buy`/`sell` line-item arrays (`def_name`, `count`, max 10 lines); handler executes programmatically via `TradeDeal` against an orbital trader/visiting caravan reachable by `trader_colonist_id`. `FormCaravan` is M5-stretch; if cut, remove tool from Strategic tier rather than shipping half-working.
- **G-21 CreateZone ergonomics:** LLMs are bad at raw cell arrays. Parameters change to rect form: `x, z, width, height` (max 20×20) + `zone_type` + optional `label`. Multi-rect zones = multiple commands.
- **G-22 Settings split:** `BudgetConfig`, autonomy level, model names = **per-save** (`AgentGameComponent.ExposeData`) so different colonies can differ; global ModSettings holds only defaults applied at new-game time. 05 §7 note added.
- **G-23 EventBus static lifecycle:** static sink list survives returning to main menu. `AgentGameComponent.FinalizeInit()` must `EventBus.Clear()` then re-subscribe; `Clear()` added to the EventBus contract.
- **G-24 Map selection policy:** all layers use `Find.AnyPlayerHomeMap` (not `Find.CurrentMap`, which follows the camera). Multi-colony support out of scope for v1; snapshot notes the map id.
- **G-25 Token estimator for `CanAfford`:** estimate = `promptChars/4 + MaxToolCallRounds × 500`. Defined here so Orchestration and Cognition agree.
- **G-26 Prompt/tool resource authoring is real work:** `Resources/Prompts/{Reactive,Tactical,Strategic}System.txt` + `Resources/Tools/*.json` (41 schemas) must be authored and `<EmbeddedResource>`-declared in the csproj. Filed as its own M3/M4 task — not an implementation footnote.
- **G-27 Localization:** English-only v1; wrap user-facing UI strings in RimWorld `Translate()` keys from the start so localization is a data task later.
- **G-28 Dev tooling for M2:** scripted playback needs a dev-mode action `RimAI: Run command file` reading `ActionCommands.json` from the config folder, and `RimAI: Dump snapshot` (already in 01). Both are debug-actions (`[DebugAction]`), spec'd here so M2 has a definite harness.

---

## 4. Consolidated milestone deltas

| Milestone | Change |
|---|---|
| **M0.5 (new)** | Framework: usage fields + translator plumbing (G-01). Small, do before M3; M1/M2 don't need it. |
| **M1** | + Harmony/csproj/About.xml fixes (G-05) FIRST · + test project (G-06) · + snapshot entity coverage (G-02) · + EntityId minting (G-03) · + EventBus.Clear (G-23) · map policy (G-24) |
| **M2** | Core-12 handlers only (G-13) · IEntityResolver (G-03) · command-file dev harness (G-28) · rect zones (G-21) |
| **M3** | + reactive command handlers (G-13) · honest tool-ack wording (G-14) · reactive cooldown exemption (G-15) · concurrency rule + staleness guard (G-17) · circuit breaker (G-18) · prompt resources for Reactive (G-26) |
| **M4** | + construction family (G-04) · + SetPlantToGrow · + remaining tactical handlers + tools (G-12) · tactical/strategic prompts+schemas (G-26) |
| **M5** | + trade line-items (G-20) · + SetPrisonerInteraction · FormCaravan = stretch |
| **M6** | + Advisor suggestion surface (G-19) · settings split (G-22) |

## 5. Corrections applied directly to 00–05 in this commit

00: M2 exit criteria = core-12; M0.5 row added. 01: snapshot v2 entity DTOs (G-02), ID rule pointer, EventBus.Clear, map policy. 02: CognitionTier rename, PerceptionEvent replaces GameAlert, tool-catalog additions, loop wording+ordering fix. 03: enum additions (G-04), count fix, phased-handler note, rect zones, designation enum. 05: tick math, GameComponentTick, reactive spacing exemption, queue-drop-on-load, concurrency, circuit breaker, CognitionTier canonical note.

Skeleton code renames (`PlanningTier`→`CognitionTier` etc.) are **not** done in this pass — they are the first task of whichever milestone touches the file, tracked per-issue. Docs are the contract; stubs follow at implementation time.
