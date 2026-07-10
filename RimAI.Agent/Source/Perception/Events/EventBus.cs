using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace RimAI.Agent.Perception
{
    /// <summary>
    /// Receives PerceptionEvents from the EventBus synchronously on the main thread.
    /// Subscribers must NOT subscribe or unsubscribe during ReceiveEvent (the bus
    /// iterates a snapshot copy to prevent ConcurrentModification).
    /// </summary>
    public interface IEventSink
    {
        void ReceiveEvent(PerceptionEvent e);
    }

    /// <summary>
    /// Static publish/subscribe bus decoupling game event sources from AI consumers.
    ///
    /// Threading model:
    ///   - Subscribe / Unsubscribe / Publish / DrainDeferred / Clear — main-thread only.
    ///   - PublishDeferred — thread-safe (ConcurrentQueue), for Harmony callbacks that
    ///     may fire off-thread. Drained on the next main-tick via DrainDeferred.
    ///
    /// Bounded queue: max 1000 deferred events; oldest dropped when full to prevent
    /// memory leaks under event storms.
    ///
    /// G-23: Clear() drops all sinks and queued events, called on FinalizeInit + map
    ///       transitions to prevent stale static sinks surviving across game loads.
    /// </summary>
    public static class EventBus
    {
        private const int MaxDeferredQueueSize = 1000;

        private static readonly object _lock = new object();
        private static readonly List<IEventSink> _sinks = new List<IEventSink>();
        private static readonly ConcurrentQueue<PerceptionEvent> _deferredQueue = new ConcurrentQueue<PerceptionEvent>();

        /// <summary>Number of currently registered sinks (for diagnostics).</summary>
        public static int SinkCount
        {
            get { lock (_lock) { return _sinks.Count; } }
        }

        /// <summary>Approximate count of pending deferred events.</summary>
        public static int DeferredCount => _deferredQueue.Count;

        /// <summary>
        /// Register a sink. Must be called on the main thread.
        /// Idempotent: a sink already registered is not added twice.
        /// </summary>
        public static void Subscribe(IEventSink sink)
        {
            if (sink == null) throw new ArgumentNullException(nameof(sink));
            lock (_lock)
            {
                if (!_sinks.Contains(sink))
                    _sinks.Add(sink);
            }
        }

        /// <summary>
        /// Remove a sink. Must be called on the main thread.
        /// Safe to call even if sink was never subscribed.
        /// </summary>
        public static void Unsubscribe(IEventSink sink)
        {
            if (sink == null) return;
            lock (_lock)
            {
                _sinks.Remove(sink);
            }
        }

        /// <summary>
        /// Synchronous broadcast to all registered sinks. Must be called on the main thread.
        /// Takes a snapshot-copy of the sink list under lock, then iterates the copy so
        /// sinks are safe to subscribe/unsubscribe during their ReceiveEvent callback.
        /// </summary>
        public static void Publish(PerceptionEvent e)
        {
            if (e == null) throw new ArgumentNullException(nameof(e));

            IEventSink[] snapshot;
            lock (_lock)
            {
                snapshot = _sinks.ToArray();
            }

            foreach (var sink in snapshot)
            {
                sink.ReceiveEvent(e);
            }
        }

        /// <summary>
        /// Thread-safe enqueue for later delivery. Harmony patches call this from any thread.
        /// Dropped if the queue is at capacity (oldest event discarded first).
        /// </summary>
        public static void PublishDeferred(PerceptionEvent e)
        {
            if (e == null) throw new ArgumentNullException(nameof(e));

            _deferredQueue.Enqueue(e);

            // Bounded: if over capacity, try to shed oldest events.
            // Count is an O(N) snapshot — we only check when the queue is likely large.
            while (_deferredQueue.Count > MaxDeferredQueueSize)
            {
                if (!_deferredQueue.TryDequeue(out _))
                    break; // another thread drained it
            }
        }

        /// <summary>
        /// Drain all deferred events and publish each. Must be called on the main thread
        /// (typically from a GameComponent tick). Publishes in FIFO order.
        /// </summary>
        public static void DrainDeferred()
        {
            // Dequeue all into a local list to avoid holding the queue during Publish.
            var batch = new List<PerceptionEvent>();
            while (_deferredQueue.TryDequeue(out var evt))
            {
                batch.Add(evt);
            }

            foreach (var evt in batch)
            {
                Publish(evt);
            }
        }

        /// <summary>
        /// G-23: Clear all sinks and pending deferred events. Called on FinalizeInit and
        /// map transitions to prevent stale static sinks from surviving across game loads.
        /// After Clear(), re-subscribe is required for the new game/map context.
        /// </summary>
        public static void Clear()
        {
            lock (_lock)
            {
                _sinks.Clear();
            }

            // Drain and discard all deferred events
            while (_deferredQueue.TryDequeue(out _)) { }
        }
    }
}
