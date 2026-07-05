using System.Collections.Concurrent;
using System.Collections.Generic;

namespace RimAI.Agent.Perception
{
    // TODO(impl): IEventSink - single method ReceiveEvent(PerceptionEvent e), called synchronously on main thread by EventBus.
    //   Subscribers must not unsubscribe during ReceiveEvent (snapshot-copied sink list during iteration).
    //   Implemented by IMemoryLogger (for event logging) and IOrchestratorSink (for reactive trigger evaluation).
    public interface IEventSink
    {
        void ReceiveEvent(PerceptionEvent e);
    }

    // TODO(impl): EventBus - static class decoupling game event sources from AI consumers.
    //   Subscribe(IEventSink) registers a sink; Unsubscribe(IEventSink) removes it.
    //   Publish(PerceptionEvent) — synchronous, main-thread only; iterates snapshot-copied sink list.
    //   PublishDeferred(PerceptionEvent) — thread-safe via ConcurrentQueue<PerceptionEvent>; drained on next main-tick by AgentGameComponent phase 1.
    //   Sinks must not subscribe/unsubscribe during Publish iteration (uses copy-on-iterate pattern).
    public static class EventBus
    {
        private static readonly List<IEventSink> _sinks = new List<IEventSink>();
        private static readonly ConcurrentQueue<PerceptionEvent> _deferredQueue = new ConcurrentQueue<PerceptionEvent>();

        // TODO(impl): Subscribe — add sink to _sinks list; must be called on main thread
        public static void Subscribe(IEventSink sink)
        {
            throw new System.NotImplementedException();
        }

        // TODO(impl): Unsubscribe — remove sink from _sinks list; must be called on main thread
        public static void Unsubscribe(IEventSink sink)
        {
            throw new System.NotImplementedException();
        }

        // TODO(impl): Publish — synchronous main-thread broadcast; snapshot-copies sink list before iteration to prevent modification during callback
        public static void Publish(PerceptionEvent e)
        {
            throw new System.NotImplementedException();
        }

        // TODO(impl): PublishDeferred — thread-safe enqueue via ConcurrentQueue; Harmony patches call this from off-thread
        public static void PublishDeferred(PerceptionEvent e)
        {
            throw new System.NotImplementedException();
        }

        // TODO(impl): DrainDeferred — called on main thread each tick by AgentGameComponent; dequeues all and calls Publish for each
        public static void DrainDeferred()
        {
            throw new System.NotImplementedException();
        }
    }

    // TODO(impl): IPerceptionEngine - main-thread snapshot-taking bridge from game state to AI pipeline.
    //   TakeSnapshot(ColonySnapshot prev) returns ColonySnapshot — reads Verse/Pawn/Map, produces DTO, never writes game state.
    //   DiffSnapshots(ColonySnapshot prev, ColonySnapshot current) returns SnapshotDiff.
    //   All Verse types stay inside implementation; callers only see DTOs.
    //   Snapshot budget ~4K tokens (≈14–18KB minified JSON for 5-colonist colony).
    public interface IPerceptionEngine
    {
        // TODO(impl): TakeSnapshot — read-only main-thread snapshot from Find.CurrentMap.
        //   Returns ColonySnapshot with all sub-DTOs populated. Handles empty colony (Pawns empty list, not null).
        //   Caps lists: 8 colonists + 5 hostiles + 5 animals max; resources 30 max; rooms 5 max.
        //   Null fields omitted by JSON serialization (JsonIgnoreCondition.WhenWritingNull).
        ColonySnapshot TakeSnapshot(ColonySnapshot prev);

        // TODO(impl): DiffSnapshots — compute delta between two snapshots. Identifies added/removed/changed pawns + resource deltas.
        //   Uses long for chronological comparisons (tick overflow safety).
        SnapshotDiff DiffSnapshots(ColonySnapshot prev, ColonySnapshot current);
    }
}
