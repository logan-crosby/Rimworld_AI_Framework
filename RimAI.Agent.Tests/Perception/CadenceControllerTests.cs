using System;
using RimAI.Agent.Perception;
using Xunit;

namespace RimAI.Agent.Tests.Perception
{
    /// <summary>
    /// Tests for CadenceController: snapshot cadence (§3.1), cognition-tier
    /// cadence (reactive/tactical/strategic), force-on-event behavior,
    /// and injectable clock.
    /// </summary>
    public class CadenceControllerTests
    {
        /// <summary>A test clock that can be advanced programmatically.</summary>
        private class TestClock : IClock
        {
            public int CurrentTick { get; set; }
        }

        // ── Snapshot cadence: guaranteed interval ────────────────────────────

        [Fact]
        public void ShouldSnapshot_FirstCall_ReturnsTrue()
        {
            var clock = new TestClock { CurrentTick = 0 };
            var controller = new CadenceController(clock);

            Assert.True(controller.ShouldSnapshot());
        }

        [Fact]
        public void ShouldSnapshot_AfterGuaranteedInterval_ReturnsTrue()
        {
            var clock = new TestClock { CurrentTick = 0 };
            var controller = new CadenceController(clock);

            controller.NotifySnapshotTaken(0);

            // Just before guarantee
            clock.CurrentTick = CadenceController.SnapshotGuaranteedIntervalTicks - 1;
            Assert.True(controller.ShouldSnapshot()); // within normal window (beyond min)

            // At guarantee boundary
            controller.NotifySnapshotTaken(clock.CurrentTick);

            clock.CurrentTick += CadenceController.SnapshotGuaranteedIntervalTicks;
            Assert.True(controller.ShouldSnapshot());
        }

        // ── Snapshot cadence: minimum throttle ───────────────────────────────

        [Fact]
        public void ShouldSnapshot_WithinMinimumInterval_ReturnsFalse()
        {
            var clock = new TestClock { CurrentTick = 0 };
            var controller = new CadenceController(clock);

            controller.NotifySnapshotTaken(0);

            // Just inside throttle window
            clock.CurrentTick = CadenceController.SnapshotMinimumIntervalTicks - 1;
            Assert.False(controller.ShouldSnapshot());
        }

        [Fact]
        public void ShouldSnapshot_AtMinimumIntervalBoundary_ReturnsTrue()
        {
            var clock = new TestClock { CurrentTick = 0 };
            var controller = new CadenceController(clock);

            controller.NotifySnapshotTaken(0);

            clock.CurrentTick = CadenceController.SnapshotMinimumIntervalTicks;
            Assert.True(controller.ShouldSnapshot());
        }

        [Fact]
        public void ShouldSnapshot_BetweenMinAndGuaranteed_ReturnsTrue()
        {
            var clock = new TestClock { CurrentTick = 0 };
            var controller = new CadenceController(clock);

            controller.NotifySnapshotTaken(0);

            clock.CurrentTick = CadenceController.SnapshotMinimumIntervalTicks + 100;
            Assert.True(controller.ShouldSnapshot());
        }

        [Fact]
        public void ShouldSnapshot_AfterNotifySnapshotTaken_ResetsThrottle()
        {
            var clock = new TestClock { CurrentTick = 1000 };
            var controller = new CadenceController(clock);

            controller.NotifySnapshotTaken(1000);

            // Within throttle after snapshot
            clock.CurrentTick = 1100; // 100 ticks elapsed < 250
            Assert.False(controller.ShouldSnapshot());
        }

        // ── Force snapshot on high-criticality event ──────────────────────────

        [Fact]
        public void NotifyEvent_HighCriticality_ForcesNextSnapshot()
        {
            var clock = new TestClock { CurrentTick = 0 };
            var controller = new CadenceController(clock);

            controller.NotifySnapshotTaken(0);

            // Within throttle window — normally false
            clock.CurrentTick = 100;
            Assert.False(controller.ShouldSnapshot());

            // High-criticality event should force
            var raidEvent = PerceptionEvent.Create(
                type: PerceptionEventType.RaidDetected,
                criticality: CriticalLevel.High,
                tick: 100,
                gameHour: 4.5f,
                mapId: 1,
                message: "Raid!");

            controller.NotifyEvent(raidEvent);

            // Now should return true despite being within throttle
            Assert.True(controller.ShouldSnapshot());
            Assert.True(controller.IsForceNextSnapshot);
        }

        [Fact]
        public void NotifyEvent_LowCriticality_DoesNotForce()
        {
            var clock = new TestClock { CurrentTick = 0 };
            var controller = new CadenceController(clock);

            controller.NotifySnapshotTaken(0);

            // Within throttle window — normally false
            clock.CurrentTick = 100;

            var letterEvent = PerceptionEvent.Create(
                type: PerceptionEventType.Letter,
                criticality: CriticalLevel.Low,
                tick: 100,
                gameHour: 4.5f,
                mapId: 1,
                message: "A trader arrived");

            controller.NotifyEvent(letterEvent);

            // Should still be false (inside throttle, no force)
            Assert.False(controller.ShouldSnapshot());
            Assert.False(controller.IsForceNextSnapshot);
        }

        [Fact]
        public void NotifyEvent_MediumCriticality_DoesNotForce()
        {
            var clock = new TestClock { CurrentTick = 0 };
            var controller = new CadenceController(clock);

            controller.NotifySnapshotTaken(0);
            clock.CurrentTick = 100;

            var event_ = PerceptionEvent.Create(
                type: PerceptionEventType.FireStarted,
                criticality: CriticalLevel.Medium,
                tick: 100,
                gameHour: 4.5f,
                mapId: 1,
                message: "Fire!");

            controller.NotifyEvent(event_);

            Assert.False(controller.ShouldSnapshot());
        }

        [Fact]
        public void NotifyEvent_NullThrows()
        {
            var clock = new TestClock { CurrentTick = 0 };
            var controller = new CadenceController(clock);

            Assert.Throws<ArgumentNullException>(() => controller.NotifyEvent(null));
        }

        [Fact]
        public void NotifySnapshotTaken_AfterForce_ClearsFlag()
        {
            var clock = new TestClock { CurrentTick = 0 };
            var controller = new CadenceController(clock);

            // Force via event
            var raidEvent = PerceptionEvent.Create(
                type: PerceptionEventType.RaidDetected,
                criticality: CriticalLevel.High,
                tick: 0,
                gameHour: 4.5f,
                mapId: 1,
                message: "Raid!");

            controller.NotifyEvent(raidEvent);
            Assert.True(controller.IsForceNextSnapshot);

            // After snapshot, flag should clear
            controller.NotifySnapshotTaken(1);
            Assert.False(controller.IsForceNextSnapshot);
        }

        // ── Reactive tier cadence ────────────────────────────────────────────

        [Fact]
        public void ShouldFireReactive_FirstCall_ReturnsTrue()
        {
            var clock = new TestClock { CurrentTick = 0 };
            var controller = new CadenceController(clock);

            Assert.True(controller.ShouldFireReactive());
        }

        [Fact]
        public void ShouldFireReactive_AfterInterval_ReturnsTrue()
        {
            var clock = new TestClock { CurrentTick = 0 };
            var controller = new CadenceController(clock);

            controller.NotifyReactiveFired(0);

            clock.CurrentTick = CadenceController.ReactiveIntervalTicks - 1;
            Assert.False(controller.ShouldFireReactive());

            clock.CurrentTick = CadenceController.ReactiveIntervalTicks;
            Assert.True(controller.ShouldFireReactive());
        }

        [Fact]
        public void NotifyReactiveFired_ResetsTimer()
        {
            var clock = new TestClock { CurrentTick = 100 };
            var controller = new CadenceController(clock);

            controller.NotifyReactiveFired(100);

            clock.CurrentTick = 150; // Only 50 ticks elapsed
            Assert.False(controller.ShouldFireReactive());
        }

        // ── Tactical tier cadence ────────────────────────────────────────────

        [Fact]
        public void ShouldFireTactical_FirstCall_ReturnsTrue()
        {
            var clock = new TestClock { CurrentTick = 0 };
            var controller = new CadenceController(clock);

            Assert.True(controller.ShouldFireTactical());
        }

        [Fact]
        public void ShouldFireTactical_AfterInterval_ReturnsTrue()
        {
            var clock = new TestClock { CurrentTick = 0 };
            var controller = new CadenceController(clock);

            controller.NotifyTacticalFired(0);

            clock.CurrentTick = CadenceController.TacticalIntervalTicks - 1;
            Assert.False(controller.ShouldFireTactical());

            clock.CurrentTick = CadenceController.TacticalIntervalTicks;
            Assert.True(controller.ShouldFireTactical());
        }

        // ── Strategic tier cadence ───────────────────────────────────────────

        [Fact]
        public void ShouldFireStrategic_FirstCall_ReturnsTrue()
        {
            var clock = new TestClock { CurrentTick = 0 };
            var controller = new CadenceController(clock);

            Assert.True(controller.ShouldFireStrategic());
        }

        [Fact]
        public void ShouldFireStrategic_AfterInterval_ReturnsTrue()
        {
            var clock = new TestClock { CurrentTick = 0 };
            var controller = new CadenceController(clock);

            controller.NotifyStrategicFired(0);

            clock.CurrentTick = CadenceController.StrategicIntervalTicks - 1;
            Assert.False(controller.ShouldFireStrategic());

            clock.CurrentTick = CadenceController.StrategicIntervalTicks;
            Assert.True(controller.ShouldFireStrategic());
        }

        // ── Tier independence ────────────────────────────────────────────────

        [Fact]
        public void TiersAreIndependent()
        {
            var clock = new TestClock { CurrentTick = 0 };
            var controller = new CadenceController(clock);

            // All three start as true
            Assert.True(controller.ShouldFireReactive());
            Assert.True(controller.ShouldFireTactical());
            Assert.True(controller.ShouldFireStrategic());

            // Fire tactical and strategic early, but not reactive
            controller.NotifyTacticalFired(0);
            controller.NotifyStrategicFired(0);

            // Reactive was never fired, so it should still be true
            clock.CurrentTick = CadenceController.ReactiveIntervalTicks;
            Assert.True(controller.ShouldFireReactive());

            // Tactical was fired at 0, not yet past interval
            Assert.False(controller.ShouldFireTactical());

            // Strategic was fired at 0, not yet past interval
            Assert.False(controller.ShouldFireStrategic());
        }

        // ── Injectable clock ──────────────────────────────────────────────────

        [Fact]
        public void Constructor_NullClock_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => new CadenceController(null));
        }

        [Fact]
        public void Clock_AdvancingTick_DrivesCadence()
        {
            var clock = new TestClock { CurrentTick = 0 };
            var controller = new CadenceController(clock);

            // Simulate a game loop
            for (int tick = 0; tick < 10000; tick++)
            {
                clock.CurrentTick = tick;

                if (controller.ShouldSnapshot())
                {
                    controller.NotifySnapshotTaken(tick);
                }

                if (controller.ShouldFireReactive())
                {
                    controller.NotifyReactiveFired(tick);
                }

                if (controller.ShouldFireTactical())
                {
                    controller.NotifyTacticalFired(tick);
                }

                if (controller.ShouldFireStrategic())
                {
                    controller.NotifyStrategicFired(tick);
                }
            }

            // After 10000 ticks: at least 4 guaranteed snapshots (0, 2500, 5000, 7500)
            // The final snapshot tick should be >= 7500
            Assert.True(controller.LastSnapshotTick >= 7500,
                $"Expected last snapshot tick >= 7500, got {controller.LastSnapshotTick}");
        }

        // ── TicksUntilGuaranteedSnapshot ──────────────────────────────────────

        [Fact]
        public void TicksUntilGuaranteedSnapshot_ReturnsCorrectRemaining()
        {
            var clock = new TestClock { CurrentTick = 0 };
            var controller = new CadenceController(clock);

            controller.NotifySnapshotTaken(0);
            clock.CurrentTick = 1000;

            var remaining = controller.TicksUntilGuaranteedSnapshot(1000);
            Assert.Equal(
                CadenceController.SnapshotGuaranteedIntervalTicks - 1000,
                remaining);
        }

        [Fact]
        public void TicksUntilGuaranteedSnapshot_NoPriorSnapshot_ReturnsZero()
        {
            var clock = new TestClock { CurrentTick = 100 };
            var controller = new CadenceController(clock);

            // minValue means never snapshotted
            Assert.Equal(0, controller.TicksUntilGuaranteedSnapshot(100));
        }

        // ── Factory method coverage ──────────────────────────────────────────

        [Fact]
        public void PerceptionEventCreate_MetadataDefaultsToEmpty()
        {
            var evt = PerceptionEvent.Create(
                type: PerceptionEventType.Alert,
                criticality: CriticalLevel.Low,
                tick: 100,
                gameHour: 4f,
                mapId: 1,
                message: "Test");

            Assert.NotNull(evt.Metadata);
            Assert.Empty(evt.Metadata);
        }

        [Fact]
        public void PerceptionEventCreate_AllPropertiesSet()
        {
            var meta = new System.Collections.Generic.Dictionary<string, string>
            {
                { "key", "value" }
            };

            var evt = PerceptionEvent.Create(
                type: PerceptionEventType.PawnDied,
                criticality: CriticalLevel.High,
                tick: 5000,
                gameHour: 12.0f,
                mapId: 7,
                message: "Colonist died",
                sourcePawnId: "Pawn_123");

            evt.Metadata = meta;

            Assert.Equal(PerceptionEventType.PawnDied, evt.Type);
            Assert.Equal(CriticalLevel.High, evt.Criticality);
            Assert.Equal(5000, evt.Tick);
            Assert.Equal(12.0f, evt.GameHour);
            Assert.Equal(7, evt.MapId);
            Assert.Equal("Colonist died", evt.Message);
            Assert.Equal("Pawn_123", evt.SourcePawnId);
            Assert.Same(meta, evt.Metadata);
        }
    }
}
