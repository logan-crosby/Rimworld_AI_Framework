using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using Verse;

namespace RimAI.Agent.Action
{
    /// <summary>
    /// Public entry point for the Action layer.
    /// All game mutation flows through this interface on the main thread.
    /// </summary>
    public interface IActionExecutor
    {
        /// <summary>
        /// Enqueue commands for execution on the next main-thread tick.
        /// Thread-safe — callable from any thread (Cognition runs off-thread).
        /// </summary>
        // TODO(impl): EnqueueCommands — pushes ActionCommands into MainThreadCommandQueue via EnqueueBatch.
        //   Thread-safe; callable from Cognition's async continuations.
        //   Sorted by Priority asc then GeneratedAt for FIFO within same priority.
        //   Commands exceeding MaxQueueDepth are dropped (lowest priority first) with logged warning.
        void EnqueueCommands(IReadOnlyList<ActionCommand> commands);

        /// <summary>
        /// Drain and execute all queued commands. MUST be called from the
        /// Unity main thread (Orchestrator's GameComponent tick).
        /// Returns per-command results for feedback loop.
        /// </summary>
        // TODO(impl): ExecutePendingCommands — called each tick by AgentGameComponent phase 2.
        //   Drains MainThreadCommandQueue.DrainAll(), dispatches each command via ICommandDispatcher.
        //   Runs ICommandHandler.Validate then Execute for each; catches exceptions per-command.
        //   Runs IPostExecutionVerifier.Verify for each executed command.
        //   Returns CommandExecutionResult[] with success/failure per command.
        //   Returns empty array immediately if map is null (game loading / no colony).
        CommandExecutionResult[] ExecutePendingCommands();

        /// <summary>
        /// Number of commands currently queued awaiting execution.
        /// </summary>
        // TODO(impl): PendingCount — returns MainThreadCommandQueue.Count; thread-safe via volatile
        int PendingCount { get; }

        /// <summary>
        /// Maximum queue depth before EnqueueCommands drops overflow
        /// (with logged warning).
        /// </summary>
        // TODO(impl): MaxQueueDepth — default 100; configurable via AgentGameComponent/settings
        int MaxQueueDepth { get; set; }
    }

    /// <summary>
    /// Concrete implementation. Owns the queue, dispatcher, and verifier.
    /// Constructed by Orchestrator; lives for the game session.
    /// </summary>
    public class ActionExecutor : IActionExecutor
    {
        // TODO(impl): constructor — ICommandDispatcher dispatcher, IPostExecutionVerifier verifier,
        //   IActionLogger logger, int maxQueueDepth = 100.
        //   Creates MainThreadCommandQueue internally.

        public void EnqueueCommands(IReadOnlyList<ActionCommand> commands)
        {
            throw new NotImplementedException();
        }

        public CommandExecutionResult[] ExecutePendingCommands()
        {
            throw new NotImplementedException();
        }

        public int PendingCount
        {
            get { throw new NotImplementedException(); }
        }

        public int MaxQueueDepth
        {
            get { throw new NotImplementedException(); }
            set { throw new NotImplementedException(); }
        }
    }

    // --- Command Handler ---

    /// <summary>
    /// One handler per ActionCommandType. Executes on the main thread
    /// with full access to RimWorld APIs. Must be registered in CommandDispatcher.
    /// </summary>
    public interface ICommandHandler
    {
        /// <summary>The command type this handler processes.</summary>
        // TODO(impl): HandledType — returns the single ActionCommandType this handler owns.
        //   Used by CommandDispatcher to route commands to the correct handler.
        ActionCommandType HandledType { get; }

        /// <summary>
        /// Execute the command against RimWorld game state.
        /// All Verse/Map/Pawn lookups happen here — nowhere else in the pipeline.
        /// </summary>
        /// <param name="command">The validated ActionCommand to execute.</param>
        /// <param name="map">The current colony map (from Find.CurrentMap).</param>
        /// <returns>ExecutionResult with success/failure and verification hints.</returns>
        // TODO(impl): Execute — main-thread only. Performs the actual game mutation.
        //   Looks up pawns by ID, creates jobs, sets work priorities, modifies zones, etc.
        //   Catches all exceptions internally; returns failure result with error message.
        //   Populates VerificationHints dict for post-execution verification.
        CommandExecutionResult Execute(ActionCommand command, Map map);

        /// <summary>
        /// Validate that the command's Parameters are sufficient and target
        /// entities still exist in the current game state. Called before Execute.
        /// Returns null if valid, error string if not.
        /// </summary>
        // TODO(impl): Validate — main-thread only. Checks:
        //   1. All required parameter keys present for this command type
        //   2. Parameter types correct (string/int/bool/arrays)
        //   3. Entity references (colonist_id, zone_id, bill_id, etc.) exist in current game state
        //   4. Target cells within map bounds
        //   Returns null if valid, error string if any check fails.
        string Validate(ActionCommand command);
    }

    public class CommandExecutionResult
    {
        // TODO(impl): CommandId — matches the executed ActionCommand.CommandId for correlation
        public string CommandId { get; init; } = "";

        // TODO(impl): Type — the ActionCommandType that was executed
        public ActionCommandType Type { get; init; }

        // TODO(impl): IsSuccess — true if command executed without errors
        public bool IsSuccess { get; init; }

        // TODO(impl): ErrorMessage — populated with error description on failure, null on success
        public string ErrorMessage { get; init; }

        /// <summary>Key-value pairs describing expected post-execution state
        /// (e.g. {"colonist_123_drafted": "true", "position": "45,32"}).
        /// Used by PostExecutionVerifier.</summary>
        // TODO(impl): VerificationHints — handler-populated hints for post-execution verification.
        //   Format: "entity_key:expected_value". E.g., "colonist_123_drafted:true", "position:45,32".
        public Dictionary<string, string> VerificationHints { get; init; } = new Dictionary<string, string>();
    }

    /// <summary>
    /// Routes ActionCommand to the correct ICommandHandler by Type.
    /// </summary>
    public interface ICommandDispatcher
    {
        /// <summary>Register a handler for its HandledType.</summary>
        // TODO(impl): RegisterHandler — add handler to internal dictionary keyed by ActionCommandType.
        //   Throws if a handler for the same type is already registered (duplicate handler detection).
        //   Called at mod initialization; all 35+ handlers registered before first Dispatch.
        void RegisterHandler(ICommandHandler handler);

        /// <summary>Dispatch a command to its handler; throws if no handler registered.</summary>
        // TODO(impl): Dispatch — look up handler by command.Type; call handler.Validate then handler.Execute.
        //   If Validate returns non-null error, skip Execute and return failure result.
        //   If Execute throws, catch exception, return UnknownError result.
        //   Throws InvalidOperationException if no handler registered for command.Type.
        CommandExecutionResult Dispatch(ActionCommand command, Map map);
    }

    // --- Post-Execution Verification ---

    /// <summary>
    /// After each command executes, verifies that the intended game state change
    /// actually occurred. Catches cases where RimWorld silently rejects a command
    /// (path unreachable, target already dead, bill already completed, etc.).
    /// </summary>
    public interface IPostExecutionVerifier
    {
        /// <summary>
        /// Verify a single command's execution using its VerificationHints.
        /// Must be called on the main thread (reads game state).
        /// </summary>
        // TODO(impl): Verify — for each VerificationHint key, look up the entity and compare actual state.
        //   E.g., "colonist_123_drafted:true" → check pawn.drafter.Drafted == true.
        //   If entity no longer exists, skip that hint (marks Verified=true with note, does not fail).
        //   Returns VerificationResult with confirmed/unconfirmed hints.
        VerificationResult Verify(CommandExecutionResult executionResult, Map map);
    }

    public class VerificationResult
    {
        // TODO(impl): CommandId — matches the executed command for correlation
        public string CommandId { get; init; } = "";

        // TODO(impl): Verified — true if all checkable hints confirmed; false if any discrepancy found
        public bool Verified { get; init; }

        // TODO(impl): ExpectedStateConfirmed — true if expected state matched actual state
        public bool ExpectedStateConfirmed { get; init; }

        // TODO(impl): UnconfirmedHints — array of hint keys that did not match expected state
        public string[] UnconfirmedHints { get; init; } = Array.Empty<string>();

        // TODO(impl): DiscrepancyDescription — human-readable explanation of what didn't match, null if all matched
        public string DiscrepancyDescription { get; init; }
    }

    /// <summary>
    /// Default verifier implementation using reflection-based hint checking.
    /// </summary>
    public class PostExecutionVerifier : IPostExecutionVerifier
    {
        // TODO(impl): for each VerificationHint key, look up the entity and compare actual state to expected value.
        //   Examples:
        //     "colonist_123_drafted:true" → check pawn.drafter.Drafted
        //     "position:45,32" → check pawn.Position == new IntVec3(45,0,32)
        //     "bill_789_suspended:true" → check bill.suspended

        public VerificationResult Verify(CommandExecutionResult executionResult, Map map)
        {
            throw new NotImplementedException();
        }
    }

    // --- Main-Thread Command Queue ---

    /// <summary>
    /// Thread-safe queue between Cognition (off-thread) and Action (main thread).
    /// Uses lock-free ConcurrentQueue internally for enqueue; drain is single-threaded.
    /// </summary>
    public class MainThreadCommandQueue
    {
        private readonly ConcurrentQueue<ActionCommand> _queue = new ConcurrentQueue<ActionCommand>();
        private volatile int _count = 0;

        // TODO(impl): Enqueue — thread-safe single command enqueue. Drops if Count >= maxDepth.
        //   Increments _count via Interlocked.Increment.
        public void Enqueue(ActionCommand command, int maxDepth = 100)
        {
            throw new NotImplementedException();
        }

        // TODO(impl): EnqueueBatch — thread-safe bulk enqueue.
        //   Commands sorted by Priority asc then GeneratedAt. Drops overflow exceeding maxDepth.
        //   Increments _count via Interlocked.Add.
        public void EnqueueBatch(IReadOnlyList<ActionCommand> commands, int maxDepth = 100)
        {
            throw new NotImplementedException();
        }

        /// <summary>Drain all commands sorted by priority, returning oldest-first within
        /// same priority. MUST be called on main thread.</summary>
        // TODO(impl): DrainAll — main-thread only. Dequeues all, sorts by Priority asc then GeneratedAt asc.
        //   Resets _count to 0. Returns empty array if queue is empty.
        public ActionCommand[] DrainAll()
        {
            throw new NotImplementedException();
        }

        public int Count => _count;
    }

    // --- Feedback & Failure Reporting ---

    /// <summary>
    /// Aggregated per-tick execution report. Fed back to Cognition's WorkingMemory
    /// so the planner knows what succeeded, what failed, and why.
    /// </summary>
    public class ActionFeedback
    {
        // TODO(impl): TickExecuted — RimWorld tick number when commands were executed
        public int TickExecuted { get; init; }

        // TODO(impl): TotalCommandsReceived — number of commands received (including dropped)
        public int TotalCommandsReceived { get; init; }

        // TODO(impl): CommandsExecuted — number of commands that were attempted for execution
        public int CommandsExecuted { get; init; }

        // TODO(impl): CommandsSucceeded — number of commands that executed successfully
        public int CommandsSucceeded { get; init; }

        // TODO(impl): CommandsFailed — number of commands that failed execution or validation
        public int CommandsFailed { get; init; }

        // TODO(impl): CommandsDropped — number of commands dropped due to queue overflow
        public int CommandsDropped { get; init; }

        // TODO(impl): Failures — detailed failure reports for each failed command
        public CommandFailureReport[] Failures { get; init; } = Array.Empty<CommandFailureReport>();
    }

    public class CommandFailureReport
    {
        // TODO(impl): CommandId — the ID of the failed command
        public string CommandId { get; init; } = "";

        // TODO(impl): Type — the ActionCommandType that failed
        public ActionCommandType Type { get; init; }

        // TODO(impl): ErrorMessage — human-readable description of the failure
        public string ErrorMessage { get; init; } = "";

        // TODO(impl): Category — classification of the failure reason
        public FailureCategory Category { get; init; }

        // TODO(impl): OriginalParameters — the command parameters that were attempted
        public Dictionary<string, object> OriginalParameters { get; init; } = new Dictionary<string, object>();
    }

    public enum FailureCategory
    {
        // TODO(impl): EntityNotFound — colonist_id/item_id/zone_id doesn't exist in current map
        EntityNotFound,
        // TODO(impl): PathUnreachable — target cell/item is unreachable by assigned pawn
        PathUnreachable,
        // TODO(impl): InsufficientSkill — colonist lacks required skill level for the command
        InsufficientSkill,
        // TODO(impl): ResourceMissing — required materials/medicine not available
        ResourceMissing,
        // TODO(impl): GameStateConflict — command conflicts with current game state (e.g., already drafted)
        GameStateConflict,
        // TODO(impl): ValidationError — handler Validate() rejected parameters
        ValidationError,
        // TODO(impl): UnknownError — unexpected exception during execution
        UnknownError,
        // TODO(impl): Dropped — queue overflow, command never attempted
        Dropped
    }
}
