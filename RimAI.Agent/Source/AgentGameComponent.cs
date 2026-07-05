using Verse;

namespace RimAI.Agent
{
    /// <summary>
    /// Main-thread tick driver for the AI loop. Owns all layer singletons.
    /// Persisted state (cadence, budget, decision log) via ExposeData.
    /// </summary>
    public class AgentGameComponent : GameComponent
    {
        public AgentGameComponent(Game game) : base() { }

        public override void GameComponentUpdate()
        {
            // TODO(impl): five-phase tick —
            //   1. Drain Events: dequeue PerceptionEvents from EventBus, filter CriticalLevel >= Medium for reactive
            //   2. Process Commands: call IActionExecutor.ExecutePendingCommands() to drain marshalled queue
            //   3. Evaluate Cadence: check ShouldFireReactive/Tactical/Strategic on ICadenceScheduler
            //   4. Launch Planners: for each due tier where CanAfford(tier, estimate), fire Task.Run with CancellationToken
            //   5. Feed Results: marshall continuations to main thread via LongEventHandler.ExecuteWhenFinished,
            //      push ActionCommand[] to queue, log, deduct budget
            //   Main-thread safety: phases 1-3 synchronous; phase 4 DTO-only; phase 5 posted back to main thread
            throw new System.NotImplementedException();
        }

        public override void ExposeData()
        {
            // TODO(impl): persist orchestrator state —
            //   Scribe cadence state (LastReactiveTick, LastTacticalTick, LastStrategicTick, MinimumSpacingTicks)
            //   Scribe budget state (TokensUsedToday, CallsUsedToday, LastResetDay, ActiveTiers)
            //   Scribe decision log ring buffer (DecisionLogEntry[] with configurable capacity)
            //   On load: rebuild indexes, restore cadence from deserialized state
            //   Save compatibility: GameComponent absent if mod not loaded → no crash
            base.ExposeData();
        }

        public override void FinalizeInit()
        {
            // TODO(impl): wire up layers —
            //   Subscribe Memory.IMemoryLogger (IEventSink) to EventBus for event logging
            //   Restore cadence timers from deserialized ExposeData state
            //   Initialize ICadenceScheduler, IBudgetManager, ISafetyGuardrail singletons
            //   Wire IOrchestrator.Start() to begin the main loop
            base.FinalizeInit();
        }
    }
}
