using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using RimAI.Agent.Perception;
using Xunit;

namespace RimAI.Agent.Tests.Perception
{
    /// <summary>
    /// Tests for EventBus: subscribe/unsubscribe, publish delivery, deferred queue,
    /// bounded capacity, Clear(), and concurrency safety.
    /// </summary>
    public class EventBusTests : IDisposable
    {
        public EventBusTests()
        {
            // Ensure clean state before each test
            EventBus.Clear();
        }

        public void Dispose()
        {
            EventBus.Clear();
        }

        // ── Subscribe / Unsubscribe ────────────────────────────────────────

        [Fact]
        public void Subscribe_AddsSink()
        {
            var sink = new TestSink();
            EventBus.Subscribe(sink);
            Assert.Equal(1, EventBus.SinkCount);
        }

        [Fact]
        public void Subscribe_DuplicateIsIdempotent()
        {
            var sink = new TestSink();
            EventBus.Subscribe(sink);
            EventBus.Subscribe(sink);
            Assert.Equal(1, EventBus.SinkCount);
        }

        [Fact]
        public void Subscribe_NullThrows()
        {
            Assert.Throws<ArgumentNullException>(() => EventBus.Subscribe(null));
        }

        [Fact]
        public void Unsubscribe_RemovesSink()
        {
            var sink = new TestSink();
            EventBus.Subscribe(sink);
            EventBus.Unsubscribe(sink);
            Assert.Equal(0, EventBus.SinkCount);
        }

        [Fact]
        public void Unsubscribe_NullDoesNotThrow()
        {
            var ex = Record.Exception(() => EventBus.Unsubscribe(null));
            Assert.Null(ex);
        }

        [Fact]
        public void Unsubscribe_NotSubscribedDoesNotThrow()
        {
            var sink = new TestSink();
            var ex = Record.Exception(() => EventBus.Unsubscribe(sink));
            Assert.Null(ex);
        }

        // ── Publish ─────────────────────────────────────────────────────────

        [Fact]
        public void Publish_DeliversToAllSubscribers()
        {
            var sink1 = new TestSink();
            var sink2 = new TestSink();
            EventBus.Subscribe(sink1);
            EventBus.Subscribe(sink2);

            var evt = TestEvent(PerceptionEventType.Alert);
            EventBus.Publish(evt);

            Assert.Single(sink1.Received);
            Assert.Single(sink2.Received);
            Assert.Same(evt, sink1.Received[0]);
            Assert.Same(evt, sink2.Received[0]);
        }

        [Fact]
        public void Publish_NullThrows()
        {
            Assert.Throws<ArgumentNullException>(() => EventBus.Publish(null));
        }

        [Fact]
        public void Publish_NoSubscribersDoesNotThrow()
        {
            var evt = TestEvent(PerceptionEventType.Letter);
            var ex = Record.Exception(() => EventBus.Publish(evt));
            Assert.Null(ex);
        }

        [Fact]
        public void Publish_SinkCanSubscribeDuringIteration()
        {
            // Snapshot-copied iteration: new sinks added during Publish are not in
            // the snapshot and won't receive the in-flight event, but should not throw.
            var sink1 = new TestSink();
            var sink2 = new SubscribeOnReceiveSink();
            EventBus.Subscribe(sink1);
            EventBus.Subscribe(sink2);

            var evt = TestEvent(PerceptionEventType.Alert);
            var ex = Record.Exception(() => EventBus.Publish(evt));

            Assert.Null(ex);
            Assert.Single(sink1.Received);
            // sink2 subscribed a new sink during its callback; should not affect iteration
        }

        [Fact]
        public void Publish_SinkCanUnsubscribeDuringIteration()
        {
            // Snapshot-copied iteration: a sink that unsubscribes itself during
            // callback should not cause ConcurrentModification.
            var sink = new UnsubscribeOnReceiveSink();
            EventBus.Subscribe(sink);

            var evt = TestEvent(PerceptionEventType.Alert);
            var ex = Record.Exception(() => EventBus.Publish(evt));

            Assert.Null(ex);
            Assert.Single(sink.Received);
            // sink unsubscribed itself; subsequent Publish should not deliver to it
            EventBus.Publish(TestEvent(PerceptionEventType.Letter));
            Assert.Single(sink.Received); // still only the first event
        }

        // ── PublishDeferred + DrainDeferred ──────────────────────────────────

        [Fact]
        public void PublishDeferred_EnqueuesEvent()
        {
            var evt = TestEvent(PerceptionEventType.FireStarted);

            EventBus.PublishDeferred(evt);

            Assert.Equal(1, EventBus.DeferredCount);
        }

        [Fact]
        public void PublishDeferred_NullThrows()
        {
            Assert.Throws<ArgumentNullException>(() => EventBus.PublishDeferred(null));
        }

        [Fact]
        public void DrainDeferred_DeliversAllEnqueuedEvents()
        {
            var sink = new TestSink();
            EventBus.Subscribe(sink);

            var evt1 = TestEvent(PerceptionEventType.Letter);
            var evt2 = TestEvent(PerceptionEventType.PawnDied);
            var evt3 = TestEvent(PerceptionEventType.FireStarted);

            EventBus.PublishDeferred(evt1);
            EventBus.PublishDeferred(evt2);
            EventBus.PublishDeferred(evt3);

            EventBus.DrainDeferred();

            Assert.Equal(3, sink.Received.Count);
            Assert.Same(evt1, sink.Received[0]);
            Assert.Same(evt2, sink.Received[1]);
            Assert.Same(evt3, sink.Received[2]);
            Assert.Equal(0, EventBus.DeferredCount);
        }

        [Fact]
        public void DrainDeferred_EmptyQueueDoesNotThrow()
        {
            var ex = Record.Exception(() => EventBus.DrainDeferred());
            Assert.Null(ex);
        }

        [Fact]
        public void DrainDeferred_FifoOrderPreserved()
        {
            var sink = new TestSink();
            EventBus.Subscribe(sink);

            var events = new List<PerceptionEvent>();
            for (int i = 0; i < 100; i++)
            {
                var evt = TestEvent(PerceptionEventType.Letter, message: $"Event {i}");
                events.Add(evt);
                EventBus.PublishDeferred(evt);
            }

            EventBus.DrainDeferred();

            Assert.Equal(100, sink.Received.Count);
            for (int i = 0; i < 100; i++)
            {
                Assert.Same(events[i], sink.Received[i]);
            }
        }

        // ── Bounded Queue ────────────────────────────────────────────────────

        [Fact]
        public void PublishDeferred_BoundedQueue_DropsOldestEvents()
        {
            // Fill the queue beyond capacity; oldest events should be dropped.
            // We need to reach ~1000 events to trigger the bound.
            // For a practical test, we push many events and verify the queue
            // doesn't grow unbounded.

            for (int i = 0; i < 2000; i++)
            {
                var evt = TestEvent(PerceptionEventType.Letter, message: $"Event {i}");
                EventBus.PublishDeferred(evt);
            }

            // Queue should not exceed ~MaxDeferredQueueSize by much
            // (bounded trimming is approximate/best-effort under concurrency)
            Assert.True(EventBus.DeferredCount <= 1100,
                $"Expected DeferredCount <= 1100 but got {EventBus.DeferredCount}");
        }

        // ── Clear (G-23) ────────────────────────────────────────────────────

        [Fact]
        public void Clear_RemovesAllSinks()
        {
            EventBus.Subscribe(new TestSink());
            EventBus.Subscribe(new TestSink());
            Assert.Equal(2, EventBus.SinkCount);

            EventBus.Clear();

            Assert.Equal(0, EventBus.SinkCount);
        }

        [Fact]
        public void Clear_EmptiesDeferredQueue()
        {
            EventBus.PublishDeferred(TestEvent(PerceptionEventType.Letter));
            EventBus.PublishDeferred(TestEvent(PerceptionEventType.PawnDied));
            Assert.True(EventBus.DeferredCount > 0);

            EventBus.Clear();

            Assert.Equal(0, EventBus.DeferredCount);
        }

        [Fact]
        public void Clear_PublishedEventsNotDeliveredAfterClear()
        {
            var sink = new TestSink();
            EventBus.Subscribe(sink);
            EventBus.PublishDeferred(TestEvent(PerceptionEventType.Alert));

            EventBus.Clear();

            // After clear, the sink is gone and deferred queue is empty
            EventBus.DrainDeferred();
            Assert.Empty(sink.Received);
        }

        // ── Concurrency ─────────────────────────────────────────────────────

        [Fact]
        public async Task PublishDeferred_ConcurrentWrites_Survives()
        {
            const int threadCount = 8;
            const int eventsPerThread = 500;

            var tasks = new Task[threadCount];
            for (int t = 0; t < threadCount; t++)
            {
                int threadId = t;
                tasks[t] = Task.Run(() =>
                {
                    for (int i = 0; i < eventsPerThread; i++)
                    {
                        var evt = TestEvent(PerceptionEventType.Letter,
                            message: $"Thread {threadId} Event {i}");
                        EventBus.PublishDeferred(evt);
                    }
                });
            }

            await Task.WhenAll(tasks);

            // All events should be in the queue (or some dropped by bound).
            // The key assertion: no exceptions were thrown during concurrent writes.
            var count = EventBus.DeferredCount;
            Assert.True(count > 0, "Expected some events in queue after concurrent writes");
        }

        [Fact]
        public async Task Subscribe_ConcurrentCalls_ThreadSafe()
        {
            const int threadCount = 4;
            var sinks = new TestSink[threadCount];
            for (int i = 0; i < threadCount; i++) sinks[i] = new TestSink();

            var tasks = new Task[threadCount];
            for (int t = 0; t < threadCount; t++)
            {
                var sink = sinks[t];
                tasks[t] = Task.Run(() => EventBus.Subscribe(sink));
            }

            await Task.WhenAll(tasks);

            Assert.Equal(threadCount, EventBus.SinkCount);
        }

        [Fact]
        public void PublishDeferred_ThenDrain_Concurrent_AllDelivered()
        {
            // Concurrent enqueue then drain — verify no events lost
            var sink = new TestSink();
            EventBus.Subscribe(sink);

            const int totalEvents = 500;
            Parallel.For(0, totalEvents, i =>
            {
                EventBus.PublishDeferred(TestEvent(PerceptionEventType.Letter, message: $"Evt {i}"));
            });

            EventBus.DrainDeferred();

            Assert.Equal(totalEvents, sink.Received.Count);
        }

        // ── MapId tagging (G-24) ─────────────────────────────────────────────

        [Fact]
        public void PerceptionEvent_MapId_Preserved()
        {
            var evt = PerceptionEvent.Create(
                type: PerceptionEventType.RaidDetected,
                criticality: CriticalLevel.High,
                tick: 1000,
                gameHour: 6.5f,
                mapId: 42,
                message: "Raid detected"
            );

            Assert.Equal(42, evt.MapId);
        }

        [Fact]
        public void PerceptionEvent_MapId_DefaultValue()
        {
            var evt = PerceptionEvent.Create(
                type: PerceptionEventType.Alert,
                criticality: CriticalLevel.Low,
                tick: 500,
                gameHour: 3f,
                message: "Some alert"
            );

            Assert.Equal(-1, evt.MapId);
        }

        // ── Helpers ──────────────────────────────────────────────────────────

        private static PerceptionEvent TestEvent(
            PerceptionEventType type,
            CriticalLevel criticality = CriticalLevel.Low,
            string message = "test",
            int mapId = -1)
        {
            return PerceptionEvent.Create(
                type: type,
                criticality: criticality,
                tick: 100,
                gameHour: 4.5f,
                mapId: mapId,
                message: message);
        }

        /// <summary>Simple sink that captures received events.</summary>
        private class TestSink : IEventSink
        {
            public List<PerceptionEvent> Received { get; } = new List<PerceptionEvent>();

            public void ReceiveEvent(PerceptionEvent e)
            {
                Received.Add(e);
            }
        }

        /// <summary>Sink that subscribes a new sink during ReceiveEvent (tests snapshot-copy).</summary>
        private class SubscribeOnReceiveSink : IEventSink
        {
            private bool _didSubscribe;

            public void ReceiveEvent(PerceptionEvent e)
            {
                if (!_didSubscribe)
                {
                    _didSubscribe = true;
                    EventBus.Subscribe(new TestSink()); // should not throw
                }
            }
        }

        /// <summary>Sink that unsubscribes itself during ReceiveEvent (tests snapshot-copy).</summary>
        private class UnsubscribeOnReceiveSink : IEventSink
        {
            public List<PerceptionEvent> Received { get; } = new List<PerceptionEvent>();

            public void ReceiveEvent(PerceptionEvent e)
            {
                Received.Add(e);
                EventBus.Unsubscribe(this); // self-removal during iteration
            }
        }
    }
}
