using System;

namespace RimAI.Agent.Perception
{
    /// <summary>
    /// Injectable time source so CadenceController is pure logic and testable
    /// without depending on RimWorld's TickManager.
    /// </summary>
    public interface IClock
    {
        /// <summary>Current absolute game tick.</summary>
        int CurrentTick { get; }
    }

    /// <summary>
    /// Decides when snapshots and cognition-tier ticks should fire.
    ///
    /// Two concerns:
    ///   1. Snapshot cadence (§3.1): guaranteed every 2500 ticks, throttled at 250 ticks,
    ///      force-on-high-criticality events.
    ///   2. Cognition-tier cadence: Reactive (~60 ticks), Tactical (~1000 ticks),
    ///      Strategic (~6000 ticks). These drive the orchestration loop tiers.
    ///
    /// Pure logic — takes an IClock for testability; no Verse dependencies.
    /// </summary>
    public class CadenceController
    {
        // --- Snapshot constants (§3.1) ---
        public const int SnapshotGuaranteedIntervalTicks = 2500; // ~1 game-hour
        public const int SnapshotMinimumIntervalTicks = 250;     // throttle floor

        // --- Cognition-tier constants ---
        public const int ReactiveIntervalTicks = 60;     // ~1 real-second at 1x
        public const int TacticalIntervalTicks = 1000;   // ~16 real-seconds at 1x
        public const int StrategicIntervalTicks = 6000;  // ~100 real-seconds at 1x

        private readonly IClock _clock;

        // Snapshot state
        private int _lastSnapshotTick = int.MinValue;
        private bool _forceNextSnapshot;

        // Cognition-tier state (last tick each tier fired)
        private int _lastReactiveTick = int.MinValue;
        private int _lastTacticalTick = int.MinValue;
        private int _lastStrategicTick = int.MinValue;

        public CadenceController(IClock clock)
        {
            _clock = clock ?? throw new ArgumentNullException(nameof(clock));
        }

        // ── Snapshot cadence (§3.1) ────────────────────────────────────────

        /// <summary>
        /// Returns true if a snapshot should be taken at <paramref name="currentTick"/>.
        /// Honors the guaranteed interval, minimum throttle, and force-next flag.
        /// </summary>
        public bool ShouldSnapshot(int currentTick)
        {
            // Force flag set by a high-criticality event — bypasses throttle
            if (_forceNextSnapshot)
                return true;

            // Never snapshot within the throttle window
            if (_lastSnapshotTick != int.MinValue &&
                currentTick - _lastSnapshotTick < SnapshotMinimumIntervalTicks)
                return false;

            // Guaranteed interval always wins
            if (_lastSnapshotTick == int.MinValue ||
                currentTick - _lastSnapshotTick >= SnapshotGuaranteedIntervalTicks)
                return true;

            // Between min and guaranteed: allow (normal cadence)
            return true;
        }

        /// <summary>Convenience overload using the injected clock.</summary>
        public bool ShouldSnapshot() => ShouldSnapshot(_clock.CurrentTick);

        /// <summary>
        /// Record that a snapshot was taken at the given tick.
        /// Resets the force-next flag.
        /// </summary>
        public void NotifySnapshotTaken(int tick)
        {
            _lastSnapshotTick = tick;
            _forceNextSnapshot = false;
        }

        /// <summary>Convenience overload using the injected clock.</summary>
        public void NotifySnapshotTaken() => NotifySnapshotTaken(_clock.CurrentTick);

        /// <summary>
        /// Notify the controller of an incoming event.
        /// If the event has Criticality == High, forces the next ShouldSnapshot call to return true
        /// (bypassing the minimum throttle).
        /// </summary>
        public void NotifyEvent(PerceptionEvent e)
        {
            if (e == null) throw new ArgumentNullException(nameof(e));
            if (e.Criticality >= CriticalLevel.High)
                _forceNextSnapshot = true;
        }

        // ── Cognition-tier cadence ─────────────────────────────────────────

        /// <summary>
        /// Returns true if the reactive tier should fire at <paramref name="currentTick"/>.
        /// Reactive fires frequently — used for immediate threat response.
        /// </summary>
        public bool ShouldFireReactive(int currentTick)
        {
            if (_lastReactiveTick == int.MinValue ||
                currentTick - _lastReactiveTick >= ReactiveIntervalTicks)
                return true;
            return false;
        }

        public bool ShouldFireReactive() => ShouldFireReactive(_clock.CurrentTick);

        public void NotifyReactiveFired(int tick) { _lastReactiveTick = tick; }
        public void NotifyReactiveFired() => NotifyReactiveFired(_clock.CurrentTick);

        /// <summary>
        /// Returns true if the tactical tier should fire at <paramref name="currentTick"/>.
        /// Tactical fires at moderate frequency — used for work assignment, squad positioning.
        /// </summary>
        public bool ShouldFireTactical(int currentTick)
        {
            if (_lastTacticalTick == int.MinValue ||
                currentTick - _lastTacticalTick >= TacticalIntervalTicks)
                return true;
            return false;
        }

        public bool ShouldFireTactical() => ShouldFireTactical(_clock.CurrentTick);

        public void NotifyTacticalFired(int tick) { _lastTacticalTick = tick; }
        public void NotifyTacticalFired() => NotifyTacticalFired(_clock.CurrentTick);

        /// <summary>
        /// Returns true if the strategic tier should fire at <paramref name="currentTick"/>.
        /// Strategic fires infrequently — used for long-term planning, research goals.
        /// </summary>
        public bool ShouldFireStrategic(int currentTick)
        {
            if (_lastStrategicTick == int.MinValue ||
                currentTick - _lastStrategicTick >= StrategicIntervalTicks)
                return true;
            return false;
        }

        public bool ShouldFireStrategic() => ShouldFireStrategic(_clock.CurrentTick);

        public void NotifyStrategicFired(int tick) { _lastStrategicTick = tick; }
        public void NotifyStrategicFired() => NotifyStrategicFired(_clock.CurrentTick);

        // ── Diagnostics ────────────────────────────────────────────────────

        /// <summary>Last tick a snapshot was taken, or int.MinValue if never.</summary>
        public int LastSnapshotTick => _lastSnapshotTick;

        /// <summary>True if the next ShouldSnapshot will bypass the throttle.</summary>
        public bool IsForceNextSnapshot => _forceNextSnapshot;

        /// <summary>Ticks until the next guaranteed snapshot from <paramref name="currentTick"/>.</summary>
        public int TicksUntilGuaranteedSnapshot(int currentTick)
        {
            if (_lastSnapshotTick == int.MinValue) return 0;
            var elapsed = currentTick - _lastSnapshotTick;
            var remaining = SnapshotGuaranteedIntervalTicks - elapsed;
            return Math.Max(0, remaining);
        }
    }
}
