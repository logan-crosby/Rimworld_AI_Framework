# 01 — Perception Layer Spec

**Status:** authoritative implementation spec for M1
**Amendments (06-plan-audit.md wins):** snapshot schema **v2** adds `Buildings`, `Zones`, `Bills`, `NotableItems`, `Policies`, `Factions` DTOs — required so the LLM can reference the IDs the tool catalog demands (G-02); token budget rises to ~5K (ceiling 5,500). IDs minted via shared `EntityId` rule (G-03). `EventBus` gains `Clear()`; `FinalizeInit()` clears + re-subscribes to avoid stale static sinks across game loads (G-23). All layers read `Find.AnyPlayerHomeMap`, never `Find.CurrentMap` (G-24). Harmony package + About.xml dependency fixes are the first M1 task (G-05).
**Namespace:** `RimAI.Agent.Perception`
**Depends on:** RimAI.Framework (no other agent layers)
**Start from:** `RimAI.Agent/Source/Perception/` skeleton stubs (`// TODO(impl)` contracts)

---

## 1. Purpose

Read-only main-thread bridge from game state to AI pipeline. Snapshots colony into versioned JSON DTOs; surfaces game events via a decoupled `EventBus`. No LLM calls, no writes to game state, no Verse types escape this layer.

---

## 2. ColonySnapshot DTO Tree

All DTOs in `RimAI.Agent.Perception.DTOs`. Serialized via `System.Text.Json` with `WriteIndented=false`, `JsonIgnoreCondition.WhenWritingNull` — null fields omitted. Budget: ~4K tokens (≈14–18KB minified JSON for typical 5-colonist colony).

### 2.1 Root — `ColonySnapshot`

```csharp
public class ColonySnapshot {
    public int Version { get; init; }                // schema version, starts at 1
    public int Tick { get; init; }                   // Find.TickManager.TicksAbs
    public int GameDay { get; init; }                public float TimeOfDay { get; init; }
    public string Season { get; init; }              // "Spring", "Summer", ...
    public ColonySummary Summary { get; init; }      public List<PawnSnapshot> Pawns { get; init; }
    public List<ResourceSnapshot> Resources { get; init; }
    public MapSnapshot Map { get; init; }            public ThreatSnapshot Threats { get; init; }
    public ResearchSnapshot Research { get; init; }  public WeatherSnapshot Weather { get; init; }
}
```

### 2.2 ColonySummary

```csharp
public class ColonySummary {
    public int ColonistCount, PrisonerCount, GuestCount, AnimalCount, HostileCount { get; init; }
    public float ColonyWealth, TotalWealth, RaidPoints { get; init; }
    public List<string> ActiveIncidents { get; init; }
    public float AvgMood, AvgFoodNeed { get; init; }
}
```

### 2.3 PawnSnapshot + Sub-DTOs (null when not applicable, omitted by JSON to save tokens)

```csharp
public class PawnSnapshot {
    public string Id, Name, KindDef, FactionName { get; init; }
    public bool IsColonist, IsPrisoner, IsDowned, IsDrafted { get; init; }
    public NeedsSnapshot Needs { get; init; }          // null for non-sentient
    public SkillsSnapshot Skills { get; init; }        // null for non-colonists
    public HealthSnapshot Health { get; init; }
    public WorkSnapshot Work { get; init; }             public EquipSnapshot Equipment { get; init; }
    public PositionSnapshot Position { get; init; }     // null if not on local map
}
public class NeedsSnapshot {
    public float Mood, Food, Rest, Recreation, Comfort, Beauty, Outdoors { get; init; }
    public List<ThoughtSnapshot> TopThoughts { get; init; } // top 5 by intensity
}
public class ThoughtSnapshot {
    public string Label { get; init; }  public float MoodOffset { get; init; }  public int ExpiresInTicks { get; init; }
}
public class SkillsSnapshot { public List<SkillSnapshot> Skills { get; init; } /* top 5 by level+passion */ }
public class SkillSnapshot {
    public string DefName, Passion { get; init; }  // DefName="Shooting"; Passion="None"|"Minor"|"Major"
    public int Level { get; init; }                public float XpSinceLastLevel { get; init; }
}
public class HealthSnapshot {
    public float Consciousness, Moving, Manipulation, Talking, BloodLoss, Pain { get; init; }
    public int OpenWoundCount, MissingPartCount { get; init; }
    public List<InjurySnapshot> TopInjuries { get; init; } // top 5 by severity
}
public class InjurySnapshot {
    public string Label, BodyPartLabel { get; init; }
    public float Severity { get; init; }            public bool Bleeding, Infected { get; init; }
}
public class WorkSnapshot {
    public string CurrentJobDef, CurrentTargetLabel { get; init; }
    public Dictionary<string, int> Priorities { get; init; } // workTypeDefName -> 0–4
}
public class EquipSnapshot {
    public string WeaponLabel { get; init; }         public float WeaponAvgDps { get; init; }
    public List<string> ApparelLabels { get; init; } // top 5, outer first
    public float ArmorBlunt, ArmorSharp { get; init; }
}
public class PositionSnapshot {
    public int X, Z { get; init; }                   public string RoomLabel, AreaDef { get; init; }
}
```

### 2.4 Resource, Map, Threat, Research, Weather

```csharp
public class ResourceSnapshot {
    public string ThingDef, Category { get; init; }   // Category="Food"|"RawResource"|"Manufactured"|"Medicine"
    public int Count { get; init; }                   public bool IsPerishable { get; init; }
    public float AverageHitPoints { get; init; }
}
public class MapSnapshot {
    public string BiomeDef, TerrainHash { get; init; }   // TerrainHash: short hash for diffing
    public int MapSizeX, MapSizeZ, RoofCollapseRisks, FireCount, FilthScore, PowerNetExcess { get; init; }
    public List<RoomSnapshot> TopRooms { get; init; }    // top 5 by wealth
}
public class RoomSnapshot {
    public string Label { get; init; }  public int CellCount { get; init; }
    public float Impressiveness, Cleanliness, Temperature { get; init; }
}
public class ThreatSnapshot {
    public int HostilePawnsOnMap, DownedColonists, FiresNearBase, BreachWallCandidates { get; init; }
    public float NearestHostileDistance { get; init; }   public List<string> ActiveRaids { get; init; }
    public bool AnyMentalBreakRisk { get; init; }
}
public class ResearchSnapshot {
    public string CurrentProject { get; init; }           public float CurrentProgress { get; init; }
    public List<string> CompletedTechs { get; init; }     // last 20 defNames
    public List<string> AvailableTechs { get; init; }     // next 10 unlockable
}
public class WeatherSnapshot {
    public string WeatherDef { get; init; }
    public float WindSpeed, OutdoorTemp, IndoorTemp, ToxicFallout { get; init; }
}
```

### 2.5 Trimmer — `ColonySnapshotTrimmer` in `RimAI.Agent.Perception`

```csharp
public static class ColonySnapshotTrimmer {
    public const int MaxTokens = 4096;  public const int MaxResourceEntries = 30;
    public static string TrimToBudget(string serializedJson, int tokenBudget = MaxTokens);
    public static int EstimateTokens(string json); // chars/4
}
```
**Trim order (drop first):** resources beyond 30 → rooms beyond top 3 → pawns beyond 8 → per-pawn: WorkSnapshot → EquipSnapshot → thoughts beyond 3 → skills beyond 3.

---

## 3. Snapshot Cadence & Diffing

### 3.1 Cadence Controller

```csharp
// RimAI.Agent.Perception
public class SnapshotCadenceController {
    public const int GuaranteedIntervalTicks = 2500;  // ≈1 game-hour, snapshot regardless
    public const int MinimumIntervalTicks = 250;      // throttle
    public bool ShouldSnapshot(int currentTick);
    public void NotifySnapshotTaken(int tick);
    public void NotifyEvent(PerceptionEvent e);        // urgent events force immediate snapshot (no throttle)
}
```

### 3.2 Diff Engine

```csharp
public class SnapshotDiff {
    public int FromTick, ToTick { get; init; }
    public List<string> AddedPawns, RemovedPawns { get; init; }
    public List<PawnDelta> ChangedPawns { get; init; }       // PawnDelta: { PawnId, List<string> ChangedFields }
    public List<ResourceDelta> ResourceChanges { get; init; }  // ResourceDelta: { ThingDef, int OldCount, NewCount }
    public bool ThreatLevelChanged { get; init; }
    public string NarrativeSummary { get; init; }            // 1–2 sentence English
}
public static class SnapshotDiffEngine {
    public static SnapshotDiff Compute(ColonySnapshot prev, ColonySnapshot current);
    public static string Summarize(SnapshotDiff diff);
}
```

Diff computed every snapshot; only the `SnapshotDiff` forwarded to `EventBus` as `SnapshotDelta` event type.

---

## 4. EventBus Design

### 4.1 Core Types (`RimAI.Agent.Perception`)

```csharp
public enum PerceptionEventType {
    Letter, Alert, RaidDetected, PawnDied, PawnDowned, FireStarted,
    MentalBreak, MedicalEmergency, ManhunterPack, SiegeDetected, Infestation,
    SnapshotDelta, BudgetExceeded, AutonomyChanged, Error
}
public enum CriticalLevel { None=0, Low=1, Medium=2, High=3 /* forces snapshot */ }

public class PerceptionEvent {
    public PerceptionEventType Type { get; init; }  public CriticalLevel Criticality { get; init; }
    public int Tick { get; init; }                  public float GameHour { get; init; }
    public string SourcePawnId { get; init; }       // null = map-level
    public string Message { get; init; }            // human-readable
    public Dictionary<string, string> Metadata { get; init; }
}
```

### 4.2 EventBus + IEventSink

```csharp
public interface IEventSink { void ReceiveEvent(PerceptionEvent e); }

public static class EventBus {
    public static void Subscribe(IEventSink sink);   public static void Unsubscribe(IEventSink sink);
    public static void Publish(PerceptionEvent e);          // synchronous, main-thread only
    public static void PublishDeferred(PerceptionEvent e);  // thread-safe, ConcurrentQueue, drained next main-tick
}
```

`Publish` iterates snapshot-copied sink list (sinks must not unsubscribe during iteration). `PublishDeferred` = safety net for Harmony callbacks that may fire off-thread.

### 4.3 Harmony Hooks (all `Priority.Last` — read-only observers)

| Hook | Target | Fires |
|------|--------|-------|
| Postfix | `Find.LetterStack.ReceiveLetter` | `Letter` |
| Postfix | `Alert.AlertActive` (new only) | `Alert` |
| Postfix | `Pawn.Kill` | `PawnDied` |
| Postfix | `Pawn_HealthTracker.MakeDowned` | `PawnDowned` |
| Postfix | `GenSpawn.Spawn` (Fire filter) | `FireStarted` |
| Postfix | `MentalStateHandler.TryStartMentalState` | `MentalBreak` |
| Prefix | Bleed rate threshold check | `MedicalEmergency` |

### 4.4 Event Flow

```
RimWorld notifications → EventBus.PublishDeferred(PerceptionEvent)
    ├─► SnapshotCadenceController.NotifyEvent()  // may force snapshot
    ├─► Memory.EventLog.Append()                 // via IMemoryLogger (IEventSink)
    └─► Orchestrator.ReceiveEvent()              // via IOrchestratorSink (IEventSink)
```

---

## 5. Layer Interaction

| Layer | Direction | What | Thread Boundary |
|-------|-----------|------|-----------------|
| Memory | → | `SnapshotDiff`, `PerceptionEvent` via `EventBus` | Same-thread (subscribers) |
| Cognition | → | Serialized `ColonySnapshot` JSON + `NarrativeSummary` | Cross-thread (main→async LLM) |
| Orchestration | → | `PerceptionEvent` via `EventBus` | Same-thread |
| None | ← | Perception is source-only | — |

Perception never calls `RimAIApi` directly.

---

## 6. Acceptance Criteria — M1

- [ ] `ColonySnapshotBuilder.TakeSnapshot()` produces valid DTO from live game.
- [ ] Dev command `Dev: Dump ColonySnapshot JSON` writes minified JSON to RimWorld log.
- [ ] 5-colonist snapshot ≤4,500 tokens (≈18KB).
- [ ] `SnapshotDiffEngine.Compute(prev, curr)` correctly identifies added/removed/changed pawns + resource deltas.
- [ ] `EventBus.Publish` delivers to all `IEventSink` subscribers same tick.
- [ ] Harmony patches fire correct `PerceptionEventType` for raid letter, death, fire spawn.
- [ ] `ColonySnapshotTrimmer.TrimToBudget` drops in order without throwing.
- [ ] `dotnet build Rim_AI_Framework.sln` — 0 errors, 0 warnings.
- [ ] Unit test: DTO serialize → deserialize → all fields match.

---

## 7. Edge Cases

1. **Empty colony**: `Pawns` empty list (not null), `ColonistCount=0`. Snapshot still produced.
2. **>50 pawns**: trim to 8 colonists + 5 hostiles + 5 animals; builder caps lists.
3. **Save without mod**: `GameComponent` absent, Perception never runs. No crash.
4. **Mod removal mid-save**: `GameComponent` removed by RimWorld. No orphan data.
5. **Tick overflow**: use `long` for chronological comparisons in `SnapshotDiff` + event ordering.
6. **Harmony conflict**: `Priority.Last` runs after other mods (we're read-only).
7. **Concurrent events**: sink list snapshot-copied during `Publish` iteration.
8. **Off-thread publish**: `ConcurrentQueue` → drained next main-tick, never lost.
9. **Zero resources**: list empty not null; diff shows all as "+N".
10. **World pawns** (caravans): excluded; only current-map pawns.

---

## 8. Snapshot Engine Interface

The snapshot-taking bridge from game state to the AI pipeline. Reads Verse/Pawn/Map data on the main thread
and produces DTOs — never writes game state. All Verse types stay inside the implementation; callers only see DTOs.

```csharp
namespace RimAI.Agent.Perception {
    public interface IPerceptionEngine
    {
        /// <summary>
        /// Take a read-only main-thread snapshot from Find.CurrentMap.
        /// Returns ColonySnapshot with all sub-DTOs populated.
        /// Handles empty colony (Pawns empty list, not null).
        /// Caps lists: 8 colonists + 5 hostiles + 5 animals max; resources 30 max; rooms 5 max.
        /// Null fields omitted by JSON serialization (JsonIgnoreCondition.WhenWritingNull).
        /// Snapshot budget ~4K tokens (≈14–18KB minified JSON for 5-colonist colony).
        /// </summary>
        ColonySnapshot TakeSnapshot(ColonySnapshot prev);

        /// <summary>
        /// Compute delta between two snapshots. Identifies added/removed/changed pawns
        /// and resource deltas. Uses long for chronological comparisons (tick overflow safety).
        /// </summary>
        SnapshotDiff DiffSnapshots(ColonySnapshot prev, ColonySnapshot current);
    }
}
```

- [ ] `TakeSnapshot` returns valid DTO from Find.CurrentMap on main thread
- [ ] `DiffSnapshots` correctly identifies pawn adds/removes/changes and resource deltas
- [ ] All Verse types stay inside implementation; callers only see DTOs
- [ ] Null fields omitted by JSON serialization

---

STATUS: DOCS-A PASS — 222 lines
