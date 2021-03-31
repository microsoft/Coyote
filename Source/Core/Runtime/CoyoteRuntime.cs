﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Coyote.Actors;
using Microsoft.Coyote.IO;
using Microsoft.Coyote.Specifications;
using Microsoft.Coyote.Testing;
using CoyoteTasks = Microsoft.Coyote.Tasks;
using SyncMonitor = System.Threading.Monitor;

namespace Microsoft.Coyote.Runtime
{
    /// <summary>
    /// Runtime for controlling, scheduling and executing asynchronous operations.
    /// </summary>
    /// <remarks>
    /// Invoking scheduling methods, such as <see cref="ScheduleNextOperation(bool, bool)"/>, is thread-safe.
    /// </remarks>
    internal sealed class CoyoteRuntime : IDisposable
    {
        /// <summary>
        /// Provides access to the runtime associated with each asynchronous control flow.
        /// </summary>
        /// <remarks>
        /// In testing mode, each testing iteration uses a unique runtime instance. To safely
        /// retrieve it from static methods, we store it in each asynchronous control flow.
        /// </remarks>
        private static readonly AsyncLocal<CoyoteRuntime> AsyncLocalInstance = new AsyncLocal<CoyoteRuntime>();

        /// <summary>
        /// Provides access to the operation executing on each asynchronous control flow
        /// during systematic testing.
        /// </summary>
        private static readonly AsyncLocal<AsyncOperation> ExecutingOperation =
            new AsyncLocal<AsyncOperation>(OnAsyncLocalExecutingOperationValueChanged);

        /// <summary>
        /// The runtime executing the current operation.
        /// </summary>
        internal static CoyoteRuntime Current
        {
            get
            {
                CoyoteRuntime runtime = AsyncLocalInstance.Value;
                if (runtime is null)
                {
                    if (IsExecutionControlled)
                    {
                        ThrowUncontrolledTaskException();
                    }

                    runtime = RuntimeFactory.InstalledRuntime;
                }

                return runtime;
            }
        }

        /// <summary>
        /// If true, the program execution is controlled by the runtime to
        /// explore interleavings and sources of nondeterminism, else false.
        /// </summary>
        internal static bool IsExecutionControlled => ExecutionControlledUseCount > 0;

        /// <summary>
        /// Count of controlled execution runtimes that have been used in this process.
        /// </summary>
        private static int ExecutionControlledUseCount;

        /// <summary>
        /// The configuration used by the runtime.
        /// </summary>
        private readonly Configuration Configuration;

        /// <summary>
        /// The default actor execution context.
        /// </summary>
        internal readonly ActorExecutionContext DefaultActorExecutionContext;

        /// <summary>
        /// Responsible for checking specifications.
        /// </summary>
        private readonly SpecificationEngine SpecificationEngine;

        /// <summary>
        /// Scheduler that controls the execution of operations during testing.
        /// </summary>
        private readonly OperationScheduler Scheduler;

        /// <summary>
        /// Map from unique ids to asynchronous operations.
        /// </summary>
        private readonly Dictionary<ulong, AsyncOperation> OperationMap;

        /// <summary>
        /// Map from controlled tasks to their corresponding operations,
        /// if such an operation exists.
        /// </summary>
        private readonly ConcurrentDictionary<Task, TaskOperation> TaskMap;

        /// <summary>
        /// The program schedule trace.
        /// </summary>
        internal ScheduleTrace ScheduleTrace;

        /// <summary>
        /// The currently scheduled asynchronous operation during systematic testing.
        /// </summary>
        private AsyncOperation ScheduledOperation;

        /// <summary>
        /// The root task id.
        /// </summary>
        private readonly int? RootTaskId;

        /// <summary>
        /// The scheduler completion source.
        /// </summary>
        private readonly TaskCompletionSource<bool> CompletionSource;

        /// <summary>
        /// Object that is used to synchronize access to the scheduler.
        /// </summary>
        private readonly object SyncObject;

        /// <summary>
        /// Monotonically increasing operation id counter.
        /// </summary>
        private long OperationIdCounter;

        /// <summary>
        /// Records if the runtime is running.
        /// </summary>
        internal volatile bool IsRunning;

        /// <summary>
        /// True if the scheduler is attached to the executing program, else false.
        /// </summary>
        private bool IsAttached;

        /// <summary>
        /// Checks if the schedule has been fully explored.
        /// </summary>
        private bool HasFullyExploredSchedule;

        /// <summary>
        /// Associated with the bug report is an optional unhandled exception.
        /// </summary>
        private Exception UnhandledException;

        /// <summary>
        /// The operation scheduling policy used by the runtime.
        /// </summary>
        internal SchedulingPolicy SchedulingPolicy => this.Scheduler?.SchedulingPolicy ??
            SchedulingPolicy.None;

        /// <summary>
        /// True if a bug was found, else false.
        /// </summary>
        internal bool IsBugFound { get; private set; }

        /// <summary>
        /// Bug report.
        /// </summary>
        internal string BugReport { get; private set; }

        /// <summary>
        /// Responsible for writing to all registered <see cref="IActorRuntimeLog"/> objects.
        /// </summary>
        internal LogWriter LogWriter { get; private set; }

        /// <summary>
        /// Used to log text messages. Use <see cref="ICoyoteRuntime.SetLogger"/>
        /// to replace the logger with a custom one.
        /// </summary>
        internal ILogger Logger
        {
            get { return this.LogWriter.Logger; }
            set { using var v = this.LogWriter.SetLogger(value); }
        }

        /// <summary>
        /// Callback that is fired when an exception is thrown that includes failed assertions.
        /// </summary>
        internal event OnFailureHandler OnFailure;

        /// <summary>
        /// Timer implementing a deadlock monitor.
        /// </summary>
        private readonly Timer DeadlockMonitor;

        /// <summary>
        /// Initializes a new instance of the <see cref="CoyoteRuntime"/> class.
        /// </summary>
        internal CoyoteRuntime(Configuration configuration, IRandomValueGenerator valueGenerator)
            : this(configuration, null, valueGenerator)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="CoyoteRuntime"/> class.
        /// </summary>
        internal CoyoteRuntime(Configuration configuration, OperationScheduler scheduler)
            : this(configuration, scheduler, scheduler.ValueGenerator)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="CoyoteRuntime"/> class.
        /// </summary>
        private CoyoteRuntime(Configuration configuration, OperationScheduler scheduler, IRandomValueGenerator valueGenerator)
        {
            this.Configuration = configuration;
            this.Scheduler = scheduler;
            this.IsRunning = true;
            this.IsAttached = true;
            this.IsBugFound = false;
            this.HasFullyExploredSchedule = false;
            this.SyncObject = new object();
            this.OperationIdCounter = 0;
            this.RootTaskId = Task.CurrentId;

            this.OperationMap = new Dictionary<ulong, AsyncOperation>();
            this.TaskMap = new ConcurrentDictionary<Task, TaskOperation>();
            this.CompletionSource = new TaskCompletionSource<bool>();
            this.ScheduleTrace = new ScheduleTrace();

            if (this.SchedulingPolicy is SchedulingPolicy.Systematic)
            {
                Interlocked.Increment(ref ExecutionControlledUseCount);
            }
            else if (this.SchedulingPolicy is SchedulingPolicy.Fuzzing)
            {
                this.DeadlockMonitor = new Timer(this.CheckIfExecutionHasDeadlocked, new SchedulingActivityInfo(),
                    Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            }

            this.SpecificationEngine = new SpecificationEngine(configuration, this);
            this.Scheduler?.SetSpecificationEngine(this.SpecificationEngine);

            this.LogWriter = new LogWriter(configuration);

            this.DefaultActorExecutionContext = this.SchedulingPolicy is SchedulingPolicy.Systematic ?
                new ActorExecutionContext.Mock(configuration, this, this.SpecificationEngine, valueGenerator, this.LogWriter) :
                new ActorExecutionContext(configuration, this, this.SpecificationEngine, valueGenerator, this.LogWriter);

            Interception.ControlledThread.ClearCache();
        }

        /// <summary>
        /// Runs the specified test method.
        /// </summary>
#if !DEBUG
        [DebuggerHidden]
#endif
        internal void RunTest(Delegate testMethod, string testName)
        {
            testName = string.IsNullOrEmpty(testName) ? string.Empty : $" '{testName}'";
            this.Logger.WriteLine($"<TestLog> Running test{testName}.");
            this.Assert(testMethod != null, "Unable to execute a null test method.");
            this.Assert(Task.CurrentId != null, "The test must execute inside a controlled task.");

            if (this.SchedulingPolicy is SchedulingPolicy.Fuzzing)
            {
                this.DeadlockMonitor.Change(TimeSpan.FromMilliseconds(this.Configuration.DeadlockTimeout),
                    Timeout.InfiniteTimeSpan);

                Task.Run(async () =>
                {
                    try
                    {
                        // Update the current asynchronous control flow with the current runtime instance,
                        // allowing future retrieval in the same asynchronous call stack.
                        AssignAsyncControlFlowRuntime(this);

                        if (testMethod is Action<IActorRuntime> actionWithRuntime)
                        {
                            actionWithRuntime(this.DefaultActorExecutionContext);
                        }
                        else if (testMethod is Action action)
                        {
                            action();
                        }
                        else if (testMethod is Func<IActorRuntime, Task> functionWithRuntime)
                        {
                            await functionWithRuntime(this.DefaultActorExecutionContext);
                        }
                        else if (testMethod is Func<Task> function)
                        {
                            await function();
                        }
                        else
                        {
                            throw new InvalidOperationException($"Unsupported test delegate of type '{testMethod.GetType()}'.");
                        }

                        lock (this.SyncObject)
                        {
                            IO.Debug.WriteLine("<ScheduleDebug> Schedule explored.");
                            this.HasFullyExploredSchedule = true;
                            this.Detach(false);
                        }
                    }
                    catch (Exception ex)
                    {
                        if (!(ex is ExecutionCanceledException))
                        {
                            // Report the unhandled exception.
                            this.NotifyUnhandledException(ex, ex.Message, cancelExecution: false);
                        }
                    }
                });
            }
            else
            {
                TaskOperation op = this.CreateTaskOperation();
                Task task = new Task(() =>
                {
                    try
                    {
                        // Update the current asynchronous control flow with the current runtime instance,
                        // allowing future retrieval in the same asynchronous call stack.
                        AssignAsyncControlFlowRuntime(this);

                        this.StartOperation(op);

                        Task testMethodTask = null;
                        if (testMethod is Action<IActorRuntime> actionWithRuntime)
                        {
                            actionWithRuntime(this.DefaultActorExecutionContext);
                        }
                        else if (testMethod is Action action)
                        {
                            action();
                        }
                        else if (testMethod is Func<IActorRuntime, Task> functionWithRuntime)
                        {
                            testMethodTask = functionWithRuntime(this.DefaultActorExecutionContext);
                        }
                        else if (testMethod is Func<Task> function)
                        {
                            testMethodTask = function();
                        }
                        else if (testMethod is Func<IActorRuntime, CoyoteTasks.Task> functionWithRuntime2)
                        {
                            testMethodTask = functionWithRuntime2(this.DefaultActorExecutionContext).UncontrolledTask;
                        }
                        else if (testMethod is Func<CoyoteTasks.Task> function2)
                        {
                            testMethodTask = function2().UncontrolledTask;
                        }
                        else
                        {
                            throw new InvalidOperationException($"Unsupported test delegate of type '{testMethod.GetType()}'.");
                        }

                        if (testMethodTask != null)
                        {
                            // If the test method is asynchronous, then wait until it completes.
                            op.TryBlockUntilTaskCompletes(testMethodTask);
                            if (testMethodTask.Exception != null)
                            {
                                // The test method failed with an unhandled exception.
                                ExceptionDispatchInfo.Capture(testMethodTask.Exception).Throw();
                            }
                            else if (testMethodTask.IsCanceled)
                            {
                                throw new TaskCanceledException(testMethodTask);
                            }
                        }

                        IO.Debug.WriteLine("<ScheduleDebug> Completed operation {0} on task '{1}'.", op.Name, Task.CurrentId);
                        op.OnCompleted();

                        // Task has completed, schedule the next enabled operation, which terminates exploration.
                        this.ScheduleNextOperation();
                    }
                    catch (Exception ex)
                    {
                        this.ProcessUnhandledExceptionInOperation(op, ex);
                    }
                });

                task.Start();
                this.WaitOperationStart(op);
            }
        }

        /// <summary>
        /// Creates a new task operation.
        /// </summary>
        internal TaskOperation CreateTaskOperation(bool isDelay = false)
        {
            ulong operationId = this.GetNextOperationId();
            TaskOperation op;
            if (isDelay)
            {
                op = new TaskDelayOperation(operationId, $"TaskDelay({operationId})", this.Configuration.TimeoutDelay, this);
            }
            else
            {
                op = new TaskOperation(operationId, $"Task({operationId})", this);
            }

            this.RegisterOperation(op);
            return op;
        }

        /// <summary>
        /// Schedules the specified action to be executed asynchronously.
        /// </summary>
#if !DEBUG
        [DebuggerStepThrough]
#endif
        internal Task ScheduleAction(Action action, Task predecessor, OperationExecutionOptions options,
            bool isDelay = false, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            TaskOperation op = this.CreateTaskOperation(isDelay);
            var context = new OperationContext<Action, object>(op, action, predecessor, options, cancellationToken);
            var task = new Task(this.ExecuteOperation, context, cancellationToken);
            return this.ScheduleTaskOperation(op, task, context.ResultSource);
        }

        /// <summary>
        /// Schedules the specified function to be executed asynchronously.
        /// </summary>
#if !DEBUG
        [DebuggerStepThrough]
#endif
        internal Task<Task> ScheduleFunction(Func<Task> function, Task predecessor, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            Task<Task> task = null;
            TaskOperation op = this.CreateTaskOperation();
            var context = new AsyncOperationContext<Func<Task>, Task, Task>(op, function, task, predecessor,
                OperationExecutionOptions.None, cancellationToken);
            task = new Task<Task>(this.ExecuteOperation<Func<Task>, Task, Task>, context, cancellationToken);
            return this.ScheduleTaskOperation(op, task, context.ExecutorSource);
        }

        /// <summary>
        /// Schedules the specified function to be executed asynchronously.
        /// </summary>
#if !DEBUG
        [DebuggerStepThrough]
#endif
        internal Task<Task<TResult>> ScheduleFunction<TResult>(Func<Task<TResult>> function, Task predecessor, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            Task<TResult> task = null;
            TaskOperation op = this.CreateTaskOperation();
            var context = new AsyncOperationContext<Func<Task<TResult>>, Task<TResult>, TResult>(op, function, task, predecessor,
                OperationExecutionOptions.None, cancellationToken);
            task = new Task<TResult>(this.ExecuteOperation<Func<Task<TResult>>, Task<TResult>, TResult>, context, cancellationToken);
            return this.ScheduleTaskOperation(op, task, context.ExecutorSource);
        }

        /// <summary>
        /// Schedules the specified function to be executed asynchronously.
        /// </summary>
#if !DEBUG
        [DebuggerStepThrough]
#endif
        internal CoyoteTasks.Task ScheduleAsyncFunction(Func<CoyoteTasks.Task> function, Task predecessor, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            Task<Task> task = null;
            TaskOperation op = this.CreateTaskOperation();
            var context = new AsyncOperationContext<Func<CoyoteTasks.Task>, Task, Task>(op, function, task, predecessor,
                OperationExecutionOptions.None, cancellationToken);
            task = new Task<Task>(this.ExecuteOperation<Func<CoyoteTasks.Task>, Task, Task>, context, cancellationToken);
            return new CoyoteTasks.Task(this, this.ScheduleTaskOperation(op, task, context.ResultSource));
        }

        /// <summary>
        /// Schedules the specified function to be executed asynchronously.
        /// </summary>
#if !DEBUG
        [DebuggerStepThrough]
#endif
        internal CoyoteTasks.Task<TResult> ScheduleAsyncFunction<TResult>(Func<CoyoteTasks.Task<TResult>> function, Task predecessor,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            Task<TResult> task = null;
            TaskOperation op = this.CreateTaskOperation();
            var context = new AsyncOperationContext<Func<CoyoteTasks.Task<TResult>>, Task<TResult>, TResult>(op, function, task, predecessor,
                OperationExecutionOptions.None, cancellationToken);
            task = new Task<TResult>(this.ExecuteOperation<Func<CoyoteTasks.Task<TResult>>, Task<TResult>, TResult>, context, cancellationToken);
            return new CoyoteTasks.Task<TResult>(this, this.ScheduleTaskOperation(op, task, context.ResultSource));
        }

        /// <summary>
        /// Schedules the specified function to be executed asynchronously.
        /// </summary>
#if !DEBUG
        [DebuggerStepThrough]
#endif
        internal Task<TResult> ScheduleFunction<TResult>(Func<TResult> function, Task predecessor, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            TaskOperation op = this.CreateTaskOperation();
            var context = new OperationContext<Func<TResult>, TResult>(op, function, predecessor,
                OperationExecutionOptions.None, cancellationToken);
            var task = new Task<TResult>(this.ExecuteOperation<Func<TResult>, TResult, TResult>, context, cancellationToken);
            return this.ScheduleTaskOperation(op, task, context.ResultSource);
        }

        /// <summary>
        /// Schedules the specified task operation for execution.
        /// </summary>
        private Task<TResult> ScheduleTaskOperation<TResult>(TaskOperation op, Task task, TaskCompletionSource<TResult> tcs)
        {
            IO.Debug.WriteLine("<CreateLog> Operation '{0}' was created to execute task '{1}'.", op.Name, task.Id);
            task.Start();
            this.WaitOperationStart(op);
            this.ScheduleNextOperation();
            this.TaskMap.TryAdd(tcs.Task, op);
            return tcs.Task;
        }

        /// <summary>
        /// Execute the operation with the specified context.
        /// </summary>
        internal void ExecuteOperation(object state)
        {
            // Extract the expected operation context from the task state.
            var context = state as OperationContext<Action, object>;

            TaskOperation op = context.Operation;
            CancellationToken ct = context.CancellationToken;
            Exception exception = null;

            try
            {
                // Update the current asynchronous control flow with the current runtime instance,
                // allowing future retrieval in the same asynchronous call stack.
                AssignAsyncControlFlowRuntime(this);

                // Notify the scheduler that the operation started. This will yield execution until
                // the operation is ready to get scheduled.
                this.StartOperation(op);
                if (context.Predecessor != null)
                {
                    // If the predecessor task is asynchronous, then wait until it completes.
                    ct.ThrowIfCancellationRequested();
                    op.TryBlockUntilTaskCompletes(context.Predecessor);
                }

                if (context.Options.HasFlag(OperationExecutionOptions.YieldAtStart))
                {
                    // Try yield execution to the next operation.
                    this.ScheduleNextOperation(true);
                }

                if (op is TaskDelayOperation delayOp)
                {
                    // Try delay scheduling this operation.
                    delayOp.DelayUntilTimeout();
                }

                // Check if the operation must be canceled before starting the work.
                ct.ThrowIfCancellationRequested();

                // Start executing the work.
                context.Work();
            }
            catch (Exception ex)
            {
                if (context.Options.HasFlag(OperationExecutionOptions.FailOnException))
                {
                    this.Assert(false, "Unhandled exception. {0}", ex);
                }
                else
                {
                    // Unwrap and cache the exception to propagate it.
                    exception = UnwrapException(ex);
                    this.ReportThrownException(exception);
                }
            }
            finally
            {
                IO.Debug.WriteLine("<ScheduleDebug> Completed operation '{0}' on task '{1}'.", op.Name, Task.CurrentId);
                op.OnCompleted();

                // Set the result task completion source to notify to the awaiters that the operation
                // has been completed, and schedule the next enabled operation.
                SetTaskCompletionSource(context.ResultSource, null, exception, default);
                this.ScheduleNextOperation();
            }
        }

        /// <summary>
        /// Execute the (asynchronous) operation with the specified context.
        /// </summary>
        private TResult ExecuteOperation<TWork, TExecutor, TResult>(object state)
        {
            // Extract the expected operation context from the task state.
            var context = state as AsyncOperationContext<TWork, TExecutor, TResult> ?? state as OperationContext<TWork, TResult>;

            TaskOperation op = context.Operation;
            CancellationToken ct = context.CancellationToken;
            TResult result = default;
            Exception exception = null;

            // The operation execution logic uses two task completion sources: (1) an executor TCS and (2) a result TCS.
            // We do this to model the execution of tasks in the .NET runtime. For example, the `Task.Factory.StartNew`
            // method has different semantics from `Task.Run`, e.g. the returned task from `Task.Factory.StartNew(Func<T>)`
            // completes at the start of an asynchronous operation, so someone cannot await on it for the completion of
            // the operation. Instead, someone needs to first use `task.Unwrap()`, and then await on the unwrapped task.
            // To model this, the executor TCS completes at the start of the operation, and contains in its `task.AsyncState`
            // a reference to the result TCS. This approach allows us to implement `task.Unwrap` in a way that gives access
            // to the result TCS, which someone can then await for the asynchronous completion of the operation.

            try
            {
                // Update the current asynchronous control flow with the current runtime instance,
                // allowing future retrieval in the same asynchronous call stack.
                AssignAsyncControlFlowRuntime(this);

                // Notify the scheduler that the operation started. This will yield execution until
                // the operation is ready to get scheduled.
                this.StartOperation(op);
                if (context is AsyncOperationContext<TWork, TExecutor, TResult> asyncContext)
                {
                    // If the operation is asynchronous, then set the executor task completion source, which
                    // can be used by `UnwrapTask` to unwrap and return the task executing this operation.
                    SetTaskCompletionSource(asyncContext.ExecutorSource, asyncContext.Executor, null, ct);
                }

                if (context.Predecessor != null)
                {
                    // If the predecessor task is asynchronous, then wait until it completes.
                    ct.ThrowIfCancellationRequested();
                    op.TryBlockUntilTaskCompletes(context.Predecessor);
                }

                // Check if the operation must be canceled before starting the work.
                ct.ThrowIfCancellationRequested();

                // Start executing the (asynchronous) work.
                Task executor = null;
                if (context.Work is Func<Task<TResult>> funcWithTaskResult)
                {
                    executor = funcWithTaskResult();
                }
                else if (context.Work is Func<Task> funcWithTask)
                {
                    executor = funcWithTask();
                }
                else if (context.Work is Func<CoyoteTasks.Task<TResult>> funcWithCoyoteTaskResult)
                {
                    // TODO: temporary until we remove the custom task type.
                    executor = funcWithCoyoteTaskResult().UncontrolledTask;
                }
                else if (context.Work is Func<CoyoteTasks.Task> funcWithCoyoteTask)
                {
                    // TODO: temporary until we remove the custom task type.
                    executor = funcWithCoyoteTask().UncontrolledTask;
                }
                else if (context.Work is Func<TResult> func)
                {
                    result = func();
                }
                else
                {
                    throw new NotSupportedException($"Unable to execute work with unsupported type {context.Work.GetType()}.");
                }

                if (executor != null)
                {
                    // If the work task is asynchronous, then wait until it completes.
                    op.TryBlockUntilTaskCompletes(executor);
                    if (executor.IsFaulted)
                    {
                        // Propagate the failing exception by rethrowing it.
                        ExceptionDispatchInfo.Capture(executor.Exception).Throw();
                    }
                    else if (executor.IsCanceled)
                    {
                        if (op.Exception != null)
                        {
                            // An exception has been already captured, so propagate it.
                            ExceptionDispatchInfo.Capture(op.Exception).Throw();
                        }
                        else
                        {
                            // Wait the canceled executor (which is non-blocking as it has already completed)
                            // to throw the generated `OperationCanceledException`.
                            executor.Wait();
                        }
                    }

                    // Safely get the result without blocking as the work has completed.
                    result = executor is Task<TResult> resultTask ? resultTask.Result :
                        executor is TResult r ? r : default;
                }
            }
            catch (Exception ex)
            {
                // Unwrap and cache the exception to propagate it.
                exception = UnwrapException(ex);
                this.ReportThrownException(exception);
            }
            finally
            {
                IO.Debug.WriteLine("<ScheduleDebug> Completed operation '{0}' on task '{1}'.", op.Name, Task.CurrentId);
                op.OnCompleted();

                // Set the result task completion source to notify to the awaiters that the operation
                // has been completed, and schedule the next enabled operation.
                SetTaskCompletionSource(context.ResultSource, result, exception, default);
                this.ScheduleNextOperation();
            }

            return result;
        }

        /// <summary>
        /// Sets the specified task completion source with a result, cancelation or exception.
        /// </summary>
        private static void SetTaskCompletionSource<TResult>(TaskCompletionSource<TResult> tcs, TResult result,
            Exception ex, CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                tcs.SetCanceled();
            }
            else if (ex != null)
            {
                tcs.SetException(ex);
            }
            else
            {
                tcs.SetResult(result);
            }
        }

        /// <summary>
        /// Schedules the specified delay to be executed asynchronously.
        /// </summary>
#if !DEBUG
        [DebuggerStepThrough]
#endif
        internal Task ScheduleDelay(TimeSpan delay, CancellationToken cancellationToken)
        {
            // TODO: support cancellations during testing.
            if (delay.TotalMilliseconds is 0)
            {
                // If the delay is 0, then complete synchronously.
                return Task.CompletedTask;
            }

            // TODO: cache the dummy delay action to optimize memory.
            var options = OperationContext.CreateOperationExecutionOptions();
            return this.ScheduleAction(() => { }, null, options, true, cancellationToken);
        }

        /// <summary>
        /// Schedules the specified task awaiter continuation to be executed asynchronously.
        /// </summary>
#if !DEBUG
        [DebuggerStepThrough]
#endif
        internal void ScheduleTaskAwaiterContinuation(Task task, Action continuation)
        {
            try
            {
                var callerOp = this.GetExecutingOperation<TaskOperation>();
                if (callerOp is null)
                {
                    ThrowUncontrolledTaskException();
                }

                if (IsCurrentOperationExecutingAsynchronously())
                {
                    IO.Debug.WriteLine("<Task> '{0}' is dispatching continuation of task '{1}'.", callerOp.Name, task.Id);
                    var options = OperationContext.CreateOperationExecutionOptions();
                    this.ScheduleAction(continuation, task, options);
                    IO.Debug.WriteLine("<Task> '{0}' dispatched continuation of task '{1}'.", callerOp.Name, task.Id);
                }
                else
                {
                    IO.Debug.WriteLine("<Task> '{0}' is executing continuation of task '{1}' on task '{2}'.",
                        callerOp.Name, task.Id, Task.CurrentId);
                    continuation();
                    IO.Debug.WriteLine("<Task> '{0}' resumed after continuation of task '{1}' on task '{2}'.",
                        callerOp.Name, task.Id, Task.CurrentId);
                }
            }
            catch (ExecutionCanceledException)
            {
                IO.Debug.WriteLine("<Exception> ExecutionCanceledException was thrown from task '{0}'.", Task.CurrentId);
            }
        }

        /// <summary>
        /// Schedules the specified yield awaiter continuation to be executed asynchronously.
        /// </summary>
#if !DEBUG
        [DebuggerStepThrough]
#endif
        internal void ScheduleYieldAwaiterContinuation(Action continuation)
        {
            try
            {
                var callerOp = this.GetExecutingOperation<TaskOperation>();
                IO.Debug.WriteLine("<Task> '{0}' is executing a yield operation.", callerOp.Id);
                var options = OperationContext.CreateOperationExecutionOptions(yieldAtStart: true);
                this.ScheduleAction(continuation, null, options);
            }
            catch (ExecutionCanceledException)
            {
                IO.Debug.WriteLine("<Exception> ExecutionCanceledException was thrown from task '{0}'.", Task.CurrentId);
            }
        }

        /// <summary>
        /// Creates a controlled task that will complete when all tasks
        /// in the specified enumerable collection have completed.
        /// </summary>
#if !DEBUG
        [DebuggerStepThrough]
#endif
        internal Task WhenAllTasksCompleteAsync(Task[] tasks)
        {
            if (tasks is null)
            {
                throw new ArgumentNullException(nameof(tasks));
            }
            else if (tasks.Length is 0)
            {
                return Task.CompletedTask;
            }

            return this.ScheduleAction(() =>
            {
                var callerOp = this.GetExecutingOperation<TaskOperation>();
                callerOp.BlockUntilTasksComplete(tasks, waitAll: true);

                List<Exception> exceptions = null;
                foreach (var task in tasks)
                {
                    if (task.IsFaulted)
                    {
                        exceptions ??= new List<Exception>();
                        exceptions.Add(task.Exception is AggregateException aex ? aex.InnerException : task.Exception);
                    }
                }

                if (exceptions != null)
                {
                    throw new AggregateException(exceptions);
                }
            }, null, OperationContext.CreateOperationExecutionOptions());
        }

        /// <summary>
        /// Creates a controlled task that will complete when all tasks
        /// in the specified enumerable collection have completed.
        /// </summary>
#if !DEBUG
        [DebuggerStepThrough]
#endif
        internal Task WhenAllTasksCompleteAsync(CoyoteTasks.Task[] tasks)
        {
            if (tasks is null)
            {
                throw new ArgumentNullException(nameof(tasks));
            }
            else if (tasks.Length is 0)
            {
                return Task.CompletedTask;
            }

            return this.ScheduleAction(() =>
            {
                var callerOp = this.GetExecutingOperation<TaskOperation>();
                callerOp.BlockUntilTasksComplete(tasks, waitAll: true);

                List<Exception> exceptions = null;
                foreach (var task in tasks)
                {
                    if (task.IsFaulted)
                    {
                        exceptions ??= new List<Exception>();
                        exceptions.Add(task.Exception is AggregateException aex ? aex.InnerException : task.Exception);
                    }
                }

                if (exceptions != null)
                {
                    throw new AggregateException(exceptions);
                }
            }, null, OperationContext.CreateOperationExecutionOptions());
        }

        /// <summary>
        /// Creates a controlled task that will complete when all tasks
        /// in the specified enumerable collection have completed.
        /// </summary>
#if !DEBUG
        [DebuggerStepThrough]
#endif
        internal Task<TResult[]> WhenAllTasksCompleteAsync<TResult>(Task<TResult>[] tasks)
        {
            if (tasks is null)
            {
                throw new ArgumentNullException(nameof(tasks));
            }
            else if (tasks.Length is 0)
            {
                return Task.FromResult(Array.Empty<TResult>());
            }

            return this.ScheduleFunction(() =>
            {
                var callerOp = this.GetExecutingOperation<TaskOperation>();
                callerOp.BlockUntilTasksComplete(tasks, waitAll: true);

                List<Exception> exceptions = null;
                foreach (var task in tasks)
                {
                    if (task.IsFaulted)
                    {
                        exceptions ??= new List<Exception>();
                        exceptions.Add(task.Exception is AggregateException aex ? aex.InnerException : task.Exception);
                    }
                }

                if (exceptions != null)
                {
                    throw new AggregateException(exceptions);
                }

                int idx = 0;
                TResult[] result = new TResult[tasks.Length];
                foreach (var task in tasks)
                {
                    result[idx] = task.Result;
                    idx++;
                }

                return result;
            }, null, default);
        }

        /// <summary>
        /// Creates a controlled task that will complete when all tasks
        /// in the specified enumerable collection have completed.
        /// </summary>
#if !DEBUG
        [DebuggerStepThrough]
#endif
        internal Task<TResult[]> WhenAllTasksCompleteAsync<TResult>(CoyoteTasks.Task<TResult>[] tasks)
        {
            if (tasks is null)
            {
                throw new ArgumentNullException(nameof(tasks));
            }
            else if (tasks.Length is 0)
            {
                return Task.FromResult(Array.Empty<TResult>());
            }

            return this.ScheduleFunction(() =>
            {
                var callerOp = this.GetExecutingOperation<TaskOperation>();
                callerOp.BlockUntilTasksComplete(tasks, waitAll: true);

                List<Exception> exceptions = null;
                foreach (var task in tasks)
                {
                    if (task.IsFaulted)
                    {
                        exceptions ??= new List<Exception>();
                        exceptions.Add(task.Exception is AggregateException aex ? aex.InnerException : task.Exception);
                    }
                }

                if (exceptions != null)
                {
                    throw new AggregateException(exceptions);
                }

                int idx = 0;
                TResult[] result = new TResult[tasks.Length];
                foreach (var task in tasks)
                {
                    result[idx] = task.Result;
                    idx++;
                }

                return result;
            }, null, default);
        }

        /// <summary>
        /// Creates a controlled task that will complete when any task
        /// in the specified enumerable collection have completed.
        /// </summary>
#if !DEBUG
        [DebuggerStepThrough]
#endif
        internal Task<Task> WhenAnyTaskCompletesAsync(Task[] tasks)
        {
            if (tasks is null)
            {
                throw new ArgumentNullException(nameof(tasks));
            }
            else if (tasks.Length is 0)
            {
                throw new ArgumentException("The tasks argument contains no tasks.");
            }

            var task = this.ScheduleFunction(() =>
            {
                var callerOp = this.GetExecutingOperation<TaskOperation>();
                callerOp.BlockUntilTasksComplete(tasks, waitAll: false);

                Task result = null;
                foreach (var task in tasks)
                {
                    if (task.IsCompleted)
                    {
                        result = task;
                        break;
                    }
                }

                return Task.FromResult(result);
            }, null, default);

            return this.UnwrapTask(task);
        }

        /// <summary>
        /// Creates a controlled task that will complete when any task
        /// in the specified enumerable collection have completed.
        /// </summary>
#if !DEBUG
        [DebuggerStepThrough]
#endif
        internal CoyoteTasks.Task<CoyoteTasks.Task> WhenAnyTaskCompletesAsync(CoyoteTasks.Task[] tasks)
        {
            if (tasks is null)
            {
                throw new ArgumentNullException(nameof(tasks));
            }
            else if (tasks.Length is 0)
            {
                throw new ArgumentException("The tasks argument contains no tasks.");
            }

            return this.ScheduleAsyncFunction(() =>
            {
                var callerOp = this.GetExecutingOperation<TaskOperation>();
                callerOp.BlockUntilTasksComplete(tasks, waitAll: false);

                CoyoteTasks.Task result = null;
                foreach (var task in tasks)
                {
                    if (task.IsCompleted)
                    {
                        result = task;
                        break;
                    }
                }

                return CoyoteTasks.Task.FromResult(result);
            }, null, default);
        }

        /// <summary>
        /// Creates a controlled task that will complete when any task
        /// in the specified enumerable collection have completed.
        /// </summary>
#if !DEBUG
        [DebuggerStepThrough]
#endif
        internal Task<Task<TResult>> WhenAnyTaskCompletesAsync<TResult>(Task<TResult>[] tasks)
        {
            if (tasks is null)
            {
                throw new ArgumentNullException(nameof(tasks));
            }
            else if (tasks.Length is 0)
            {
                throw new ArgumentException("The tasks argument contains no tasks.");
            }

            var task = this.ScheduleFunction(() =>
            {
                var callerOp = this.GetExecutingOperation<TaskOperation>();
                callerOp.BlockUntilTasksComplete(tasks, waitAll: false);

                Task<TResult> result = null;
                foreach (var task in tasks)
                {
                    if (task.IsCompleted)
                    {
                        result = task;
                        break;
                    }
                }

                return Task.FromResult(result);
            }, null, default);

            return this.UnwrapTask(task);
        }

        /// <summary>
        /// Creates a controlled task that will complete when any task
        /// in the specified enumerable collection have completed.
        /// </summary>
#if !DEBUG
        [DebuggerStepThrough]
#endif
        internal CoyoteTasks.Task<CoyoteTasks.Task<TResult>> WhenAnyTaskCompletesAsync<TResult>(CoyoteTasks.Task<TResult>[] tasks)
        {
            if (tasks is null)
            {
                throw new ArgumentNullException(nameof(tasks));
            }
            else if (tasks.Length is 0)
            {
                throw new ArgumentException("The tasks argument contains no tasks.");
            }

            return this.ScheduleAsyncFunction(() =>
            {
                var callerOp = this.GetExecutingOperation<TaskOperation>();
                callerOp.BlockUntilTasksComplete(tasks, waitAll: false);

                CoyoteTasks.Task<TResult> result = null;
                foreach (var task in tasks)
                {
                    if (task.IsCompleted)
                    {
                        result = task;
                        break;
                    }
                }

                return CoyoteTasks.Task.FromResult(result);
            }, null, default);
        }

        /// <summary>
        /// Waits for all of the provided controlled task objects to complete execution within
        /// a specified number of milliseconds or until a cancellation token is cancelled.
        /// </summary>
        internal bool WaitAllTasksComplete(Task[] tasks)
        {
            // TODO: support cancellations during testing.
            if (tasks is null)
            {
                throw new ArgumentNullException(nameof(tasks));
            }
            else if (tasks.Length is 0)
            {
                return true;
            }

            var callerOp = this.GetExecutingOperation<TaskOperation>();
            callerOp.BlockUntilTasksComplete(tasks, waitAll: true);

            // TODO: support timeouts during testing, this would become false if there is a timeout.
            return true;
        }

        /// <summary>
        /// Waits for all of the provided controlled task objects to complete execution within
        /// a specified number of milliseconds or until a cancellation token is cancelled.
        /// </summary>
        internal bool WaitAllTasksComplete(CoyoteTasks.Task[] tasks)
        {
            // TODO: support cancellations during testing.
            if (tasks is null)
            {
                throw new ArgumentNullException(nameof(tasks));
            }
            else if (tasks.Length is 0)
            {
                return true;
            }

            var callerOp = this.GetExecutingOperation<TaskOperation>();
            callerOp.BlockUntilTasksComplete(tasks, waitAll: true);

            // TODO: support timeouts during testing, this would become false if there is a timeout.
            return true;
        }

        /// <summary>
        /// Waits for any of the provided controlled task objects to complete execution within
        /// a specified number of milliseconds or until a cancellation token is cancelled.
        /// </summary>
#if !DEBUG
        [DebuggerStepThrough]
#endif
        internal int WaitAnyTaskCompletes(Task[] tasks)
        {
            // TODO: support cancellations during testing.
            if (tasks is null)
            {
                throw new ArgumentNullException(nameof(tasks));
            }
            else if (tasks.Length is 0)
            {
                throw new ArgumentException("The tasks argument contains no tasks.");
            }

            var callerOp = this.GetExecutingOperation<TaskOperation>();
            callerOp.BlockUntilTasksComplete(tasks, waitAll: false);

            int result = -1;
            for (int i = 0; i < tasks.Length; i++)
            {
                if (tasks[i].IsCompleted)
                {
                    result = i;
                    break;
                }
            }

            // TODO: support timeouts during testing, this would become false if there is a timeout.
            return result;
        }

        /// <summary>
        /// Waits for any of the provided controlled task objects to complete execution within
        /// a specified number of milliseconds or until a cancellation token is cancelled.
        /// </summary>
#if !DEBUG
        [DebuggerStepThrough]
#endif
        internal int WaitAnyTaskCompletes(CoyoteTasks.Task[] tasks)
        {
            // TODO: support cancellations during testing.
            if (tasks is null)
            {
                throw new ArgumentNullException(nameof(tasks));
            }
            else if (tasks.Length is 0)
            {
                throw new ArgumentException("The tasks argument contains no tasks.");
            }

            var callerOp = this.GetExecutingOperation<TaskOperation>();
            callerOp.BlockUntilTasksComplete(tasks, waitAll: false);

            int result = -1;
            for (int i = 0; i < tasks.Length; i++)
            {
                if (tasks[i].IsCompleted)
                {
                    result = i;
                    break;
                }
            }

            // TODO: support timeouts during testing, this would become false if there is a timeout.
            return result;
        }

        /// <summary>
        /// Waits for the task to complete execution. The wait terminates if a timeout interval
        /// elapses or a cancellation token is canceled before the task completes.
        /// </summary>
        internal bool WaitTaskCompletes(Task task)
        {
            this.AssertIsAwaitedTaskControlled(task);

            // TODO: support timeouts and cancellation tokens.
            if (!task.IsCompleted)
            {
                var callerOp = this.GetExecutingOperation<TaskOperation>();
                callerOp.BlockUntilTaskCompletes(task);
            }

            if (task.IsFaulted)
            {
                // Propagate the failing exception by rethrowing it.
                ExceptionDispatchInfo.Capture(task.Exception).Throw();
            }

            return true;
        }

        /// <summary>
        /// Waits for the task to complete execution and returns the result.
        /// </summary>
        internal TResult WaitTaskCompletes<TResult>(Task<TResult> task)
        {
            this.AssertIsAwaitedTaskControlled(task);

            // TODO: support timeouts and cancellation tokens.
            if (!task.IsCompleted)
            {
                var callerOp = this.GetExecutingOperation<TaskOperation>();
                callerOp.BlockUntilTaskCompletes(task);
            }

            if (task.IsFaulted)
            {
                // Propagate the failing exception by rethrowing it.
                ExceptionDispatchInfo.Capture(task.Exception).Throw();
            }

            return task.Result;
        }

        /// <summary>
        /// Unwraps the specified task.
        /// </summary>
        internal Task UnwrapTask(Task<Task> task)
        {
            var unwrappedTask = task.AsyncState is TaskCompletionSource<Task> tcs ? tcs.Task : task.Unwrap();
            this.TaskMap.TryGetValue(task, out TaskOperation op);
            this.TaskMap.TryAdd(unwrappedTask, op);
            return unwrappedTask;
        }

        /// <summary>
        /// Unwraps the specified task.
        /// </summary>
        internal Task<TResult> UnwrapTask<TResult>(Task<Task<TResult>> task)
        {
            var unwrappedTask = task.AsyncState is TaskCompletionSource<TResult> tcs ? tcs.Task : task.Unwrap();
            this.TaskMap.TryGetValue(task, out TaskOperation op);
            this.TaskMap.TryAdd(unwrappedTask, op);
            return unwrappedTask;
        }

        /// <summary>
        /// Callback invoked when the <see cref="AsyncTaskMethodBuilder.Start"/> is accessed.
        /// </summary>
        internal void OnAsyncTaskMethodBuilderStart()
        {
            if (this.SchedulingPolicy is SchedulingPolicy.Fuzzing)
            {
                AssignAsyncControlFlowRuntime(this);
            }
        }

        /// <summary>
        /// Callback invoked when the task of a task completion source is accessed.
        /// </summary>
        internal void OnTaskCompletionSourceGetTask(Task task)
        {
            if (this.SchedulingPolicy is SchedulingPolicy.Systematic)
            {
                this.TaskMap.TryAdd(task, null);
            }
        }

        /// <summary>
        /// Callback invoked when the <see cref="AsyncTaskMethodBuilder.SetException"/> is accessed.
        /// </summary>
        internal void OnAsyncTaskMethodBuilderSetException(Exception exception)
        {
            if (this.SchedulingPolicy is SchedulingPolicy.Systematic)
            {
                var op = this.GetExecutingOperation<TaskOperation>();
                op?.SetException(exception);
            }
        }

        /// <summary>
        /// Callback invoked when the <see cref="CoyoteTasks.YieldAwaitable.YieldAwaiter.GetResult"/> is called.
        /// </summary>
#if !DEBUG
        [DebuggerStepThrough]
#endif
        internal void OnYieldAwaiterGetResult()
        {
            if (this.SchedulingPolicy is SchedulingPolicy.Systematic)
            {
                this.ScheduleNextOperation();
            }
            else if (this.SchedulingPolicy is SchedulingPolicy.Fuzzing)
            {
                this.DelayOperation();
            }
        }

        /// <summary>
        /// Callback invoked when the <see cref="CoyoteTasks.TaskAwaiter.GetResult"/> is called.
        /// </summary>
#if !DEBUG
        [DebuggerStepThrough]
#endif
        internal void OnWaitTask(Task task)
        {
            if (!task.IsCompleted)
            {
                if (this.SchedulingPolicy is SchedulingPolicy.Systematic)
                {
                    var callerOp = this.GetExecutingOperation<TaskOperation>();
                    callerOp.BlockUntilTaskCompletes(task);
                }
                else if (this.SchedulingPolicy is SchedulingPolicy.Fuzzing)
                {
                    this.DelayOperation();
                }
            }
        }

        /// <summary>
        /// Schedules the next enabled operation, which can include the currently executing operation.
        /// </summary>
        /// <param name="isYielding">True if the current operation is yielding, else false.</param>
        /// <param name="checkCaller">If true, schedule only if the caller is an operation.</param>
        /// <remarks>
        /// An enabled operation is one that is not blocked nor completed.
        /// </remarks>
        internal void ScheduleNextOperation(bool isYielding = false, bool checkCaller = false)
        {
            lock (this.SyncObject)
            {
                if (checkCaller)
                {
                    var callerOp = this.GetExecutingOperation<AsyncOperation>();
                    if (callerOp is null)
                    {
                        return;
                    }
                }

                int? taskId = Task.CurrentId;

                // TODO: figure out if this check is still needed.
                // If the caller is the root task, then return.
                if (taskId != null && taskId == this.RootTaskId)
                {
                    return;
                }

                AsyncOperation current = this.ScheduledOperation;
                this.ThrowExecutionCanceledExceptionIfDetached();
                if (current.Status != AsyncOperationStatus.Completed)
                {
                    // Checks if the current operation is controlled by the runtime.
                    if (ExecutingOperation.Value is null)
                    {
                        ThrowUncontrolledTaskException();
                    }
                }

                // Checks if the scheduling steps bound has been reached.
                this.CheckIfSchedulingStepsBoundIsReached();

                if (this.Configuration.IsProgramStateHashingEnabled)
                {
                    // Update the current operation with the hashed program state.
                    current.HashedProgramState = this.GetHashedProgramState();
                }

                // Choose the next operation to schedule, if there is one enabled.
                if (!this.TryGetNextEnabledOperation(current, isYielding, out AsyncOperation next))
                {
                    IO.Debug.WriteLine("<ScheduleDebug> Schedule explored.");
                    this.HasFullyExploredSchedule = true;
                    this.Detach();
                }

                IO.Debug.WriteLine("<ScheduleDebug> Scheduling the next operation of '{0}'.", next.Name);
                this.ScheduleTrace.AddSchedulingChoice(next.Id);
                if (current != next)
                {
                    // Pause the currently scheduled operation, and enable the next one.
                    this.ScheduledOperation = next;
                    this.PauseOperation(current);
                }
            }
        }

        /// <summary>
        /// Tries to get the next enabled operation to schedule.
        /// </summary>
        private bool TryGetNextEnabledOperation(AsyncOperation current, bool isYielding, out AsyncOperation next)
        {
            // Get and order the operations by their id.
            var ops = this.OperationMap.Values.OrderBy(op => op.Id);

            // The scheduler might need to retry choosing a next operation in the presence of uncontrolled
            // concurrency, as explained below. In this case, we implement a simple retry logic.
            int retries = 0;
            do
            {
                // Enable any blocked operation that has its dependencies already satisfied.
                IO.Debug.WriteLine("<ScheduleDebug> Enabling any blocked operation with satisfied dependencies.");
                foreach (var op in ops)
                {
                    this.TryEnableOperation(op);
                    IO.Debug.WriteLine("<ScheduleDebug> Operation '{0}' has status '{1}'.", op.Id, op.Status);
                }

                // Choose the next operation to schedule, if there is one enabled.
                if (!this.Scheduler.GetNextOperation(ops, current, isYielding, out next) &&
                    this.Configuration.IsRelaxedControlledTestingEnabled &&
                    ops.Any(op => op.IsBlockedOnUncontrolledDependency()))
                {
                    // At least one operation is blocked due to uncontrolled concurrency. To try defend against
                    // this, retry after an asynchronous delay to give some time to the dependency to complete.
                    Task.Run(async () =>
                    {
                        await Task.Delay(10);
                        lock (this.SyncObject)
                        {
                            SyncMonitor.PulseAll(this.SyncObject);
                        }
                    });

                    // Pause the current operation until the scheduler retries.
                    SyncMonitor.Wait(this.SyncObject);
                    retries++;
                    continue;
                }

                break;
            }
            while (retries < 5);

            if (next is null)
            {
                // Check if the execution has deadlocked.
                this.CheckIfExecutionHasDeadlocked(ops);
                return false;
            }

            return true;
        }

        /// <summary>
        /// Delays the currently executing operation.
        /// </summary>
        internal void DelayOperation()
        {
            lock (this.SyncObject)
            {
                this.ThrowExecutionCanceledExceptionIfDetached();

                // Update the current asynchronous control flow with the current runtime instance,
                // allowing future retrieval in the same asynchronous call stack.
                AssignAsyncControlFlowRuntime(this);

                // Choose the next delay to inject.
                int next = this.GetNondeterministicDelay((int)this.Configuration.TimeoutDelay);

                IO.Debug.WriteLine("<ScheduleDebug> Delaying the operation that executes on task '{0}' by {1}ms.", Task.CurrentId, next);
                // Thread.Sleep(next);
            }
        }

        /// <summary>
        /// Returns a controlled nondeterministic boolean choice.
        /// </summary>
        internal bool GetNondeterministicBooleanChoice(int maxValue, string callerName, string callerType) =>
            this.DefaultActorExecutionContext.GetNondeterministicBooleanChoice(maxValue, callerName, callerType);

        /// <summary>
        /// Returns the next nondeterministic boolean choice.
        /// </summary>
        internal bool GetNextNondeterministicBooleanChoice(int maxValue)
        {
            lock (this.SyncObject)
            {
                this.ThrowExecutionCanceledExceptionIfDetached();

                // Checks if the current operation is controlled by the runtime.
                if (ExecutingOperation.Value is null)
                {
                    ThrowUncontrolledTaskException();
                }

                // Checks if the scheduling steps bound has been reached.
                this.CheckIfSchedulingStepsBoundIsReached();

                if (this.Configuration.IsProgramStateHashingEnabled)
                {
                    // Update the current operation with the hashed program state.
                    this.ScheduledOperation.HashedProgramState = this.GetHashedProgramState();
                }

                if (!this.Scheduler.GetNextBooleanChoice(this.ScheduledOperation, maxValue, out bool choice))
                {
                    this.Detach();
                }

                this.ScheduleTrace.AddNondeterministicBooleanChoice(choice);
                return choice;
            }
        }

        /// <summary>
        /// Returns a controlled nondeterministic integer choice.
        /// </summary>
        internal int GetNondeterministicIntegerChoice(int maxValue, string callerName, string callerType) =>
            this.DefaultActorExecutionContext.GetNondeterministicIntegerChoice(maxValue, callerName, callerType);

        /// <summary>
        /// Returns the next nondeterministic integer choice.
        /// </summary>
        internal int GetNextNondeterministicIntegerChoice(int maxValue)
        {
            lock (this.SyncObject)
            {
                this.ThrowExecutionCanceledExceptionIfDetached();

                // Checks if the current operation is controlled by the runtime.
                if (ExecutingOperation.Value is null)
                {
                    ThrowUncontrolledTaskException();
                }

                // Checks if the scheduling steps bound has been reached.
                this.CheckIfSchedulingStepsBoundIsReached();

                if (this.Configuration.IsProgramStateHashingEnabled)
                {
                    // Update the current operation with the hashed program state.
                    this.ScheduledOperation.HashedProgramState = this.GetHashedProgramState();
                }

                if (!this.Scheduler.GetNextIntegerChoice(this.ScheduledOperation, maxValue, out int choice))
                {
                    this.Detach();
                }

                this.ScheduleTrace.AddNondeterministicIntegerChoice(choice);
                return choice;
            }
        }

        /// <summary>
        /// Returns a controlled nondeterministic delay.
        /// </summary>
        internal int GetNondeterministicDelay(int maxValue)
        {
            lock (this.SyncObject)
            {
                // Checks if the scheduling steps bound has been reached.
                this.CheckIfSchedulingStepsBoundIsReached();

                // Choose the next delay to inject.
                int maxDelay = maxValue > 0 ? (int)this.Configuration.TimeoutDelay : 1;
                if (!this.Scheduler.GetNextDelay(maxDelay, out int next))
                {
                    this.Detach();
                }

                return next;
            }
        }

        /// <summary>
        /// Registers the specified asynchronous operation.
        /// </summary>
        /// <param name="op">The operation to register.</param>
        /// <returns>True if the operation was successfully registered, else false if it already exists.</returns>
        internal bool RegisterOperation(AsyncOperation op)
        {
            lock (this.SyncObject)
            {
                if (this.OperationMap.Count is 0)
                {
                    this.ScheduledOperation = op;
                }

#if NETSTANDARD2_0
                if (!this.OperationMap.ContainsKey(op.Id))
                {
                    this.OperationMap.Add(op.Id, op);
                    return true;
                }

                return false;
#else
                return this.OperationMap.TryAdd(op.Id, op);
#endif
            }
        }

        /// <summary>
        /// Starts the execution of the specified asynchronous operation.
        /// </summary>
        /// <param name="op">The operation to start executing.</param>
        /// <remarks>
        /// This method performs a handshake with <see cref="WaitOperationStart"/>.
        /// </remarks>
        internal void StartOperation(AsyncOperation op)
        {
            lock (this.SyncObject)
            {
                IO.Debug.WriteLine("<ScheduleDebug> Starting the operation of '{0}' on task '{1}'.", op.Name, Task.CurrentId);

                // Enable the operation and store it in the async local context.
                op.Status = AsyncOperationStatus.Enabled;
                ExecutingOperation.Value = op;
                this.PauseOperation(op);
            }
        }

        /// <summary>
        /// Waits for the specified asynchronous operation to start executing.
        /// </summary>
        /// <param name="op">The operation to wait.</param>
        /// <remarks>
        /// This method performs a handshake with <see cref="StartOperation"/>.
        /// </remarks>
        internal void WaitOperationStart(AsyncOperation op)
        {
            lock (this.SyncObject)
            {
                if (this.OperationMap.Count > 1)
                {
                    while (op.Status != AsyncOperationStatus.Enabled && this.IsAttached)
                    {
                        SyncMonitor.Wait(this.SyncObject);
                    }
                }
            }
        }

        /// <summary>
        /// Pauses the specified operation.
        /// </summary>
        /// <remarks>
        /// It is assumed that this method runs in the scope of a 'lock (this.SyncObject)' statement.
        /// </remarks>
        private void PauseOperation(AsyncOperation op)
        {
            SyncMonitor.PulseAll(this.SyncObject);
            if (op.Status is AsyncOperationStatus.Completed ||
                op.Status is AsyncOperationStatus.Canceled)
            {
                // The operation is completed or canceled, so no need to wait.
                return;
            }

            while (op != this.ScheduledOperation && this.IsAttached)
            {
                IO.Debug.WriteLine("<ScheduleDebug> Sleeping the operation of '{0}' on task '{1}'.", op.Name, Task.CurrentId);
                SyncMonitor.Wait(this.SyncObject);
                IO.Debug.WriteLine("<ScheduleDebug> Waking up the operation of '{0}' on task '{1}'.", op.Name, Task.CurrentId);
            }

            this.ThrowExecutionCanceledExceptionIfDetached();
        }

        /// <summary>
        /// Tries to enable the specified operation.
        /// </summary>
        /// <remarks>
        /// It is assumed that this method runs in the scope of a 'lock (this.SyncObject)' statement.
        /// </remarks>
        private void TryEnableOperation(AsyncOperation op)
        {
            if (op.Status is AsyncOperationStatus.Delayed && op is TaskDelayOperation delayOp)
            {
                if (!delayOp.TryEnable() && !this.OperationMap.Any(kvp => kvp.Value.Status is AsyncOperationStatus.Enabled))
                {
                    op.Status = AsyncOperationStatus.Enabled;
                }
            }
            else if (op.Status != AsyncOperationStatus.Enabled)
            {
                op.TryEnable();
            }
        }

        /// <summary>
        /// Gets the <see cref="AsyncOperation"/> that is currently executing,
        /// or null if no such operation is executing.
        /// </summary>
#if !DEBUG
        [DebuggerStepThrough]
#endif
        internal TAsyncOperation GetExecutingOperation<TAsyncOperation>()
            where TAsyncOperation : AsyncOperation
        {
            lock (this.SyncObject)
            {
                this.ThrowExecutionCanceledExceptionIfDetached();

                var op = ExecutingOperation.Value;
                if (op is null)
                {
                    ThrowUncontrolledTaskException();
                }

                return op.Equals(this.ScheduledOperation) && op is TAsyncOperation expected ? expected : default;
            }
        }

        /// <summary>
        /// Gets the <see cref="AsyncOperation"/> associated with the specified
        /// unique id, or null if no such operation exists.
        /// </summary>
#if !DEBUG
        [DebuggerStepThrough]
#endif
        internal TAsyncOperation GetOperationWithId<TAsyncOperation>(ulong id)
            where TAsyncOperation : AsyncOperation
        {
            lock (this.SyncObject)
            {
                if (this.OperationMap.TryGetValue(id, out AsyncOperation op) &&
                    op is TAsyncOperation expected)
                {
                    return expected;
                }
            }

            return default;
        }

        /// <summary>
        /// Returns all registered operations.
        /// </summary>
        /// <remarks>
        /// This operation is thread safe because the systematic testing
        /// runtime serializes the execution.
        /// </remarks>
        internal IEnumerable<AsyncOperation> GetRegisteredOperations()
        {
            lock (this.SyncObject)
            {
                return this.OperationMap.Values;
            }
        }

        /// <summary>
        /// Returns the next available unique operation id.
        /// </summary>
        /// <returns>Value representing the next available unique operation id.</returns>
        internal ulong GetNextOperationId() =>
            // Atomically increments and safely wraps the value into an unsigned long.
            (ulong)Interlocked.Increment(ref this.OperationIdCounter) - 1;

        /// <summary>
        /// Returns the current hashed state of the execution.
        /// </summary>
        /// <remarks>
        /// The hash is updated in each execution step.
        /// </remarks>
        [DebuggerStepThrough]
        private int GetHashedProgramState()
        {
            unchecked
            {
                int hash = 19;
                hash = (hash * 397) + this.DefaultActorExecutionContext.GetHashedActorState();
                hash = (hash * 397) + this.SpecificationEngine.GetHashedMonitorState();
                return hash;
            }
        }

        /// <summary>
        /// Checks if the currently executing operation is controlled by the runtime.
        /// </summary>
#if !DEBUG
        [DebuggerHidden]
#endif
        internal void CheckExecutingOperationIsControlled()
        {
            if (this.SchedulingPolicy is SchedulingPolicy.Systematic)
            {
                this.GetExecutingOperation<AsyncOperation>();
            }
        }

        /// <summary>
        /// Checks if the scheduling steps bound has been reached. If yes,
        /// it stops the scheduler and kills all enabled machines.
        /// </summary>
#if !DEBUG
        [DebuggerHidden]
#endif
        private void CheckIfSchedulingStepsBoundIsReached()
        {
            if (this.Scheduler.IsMaxStepsReached)
            {
                string message = $"Scheduling steps bound of {this.Scheduler.StepCount} reached.";
                if (this.Configuration.ConsiderDepthBoundHitAsBug)
                {
                    this.NotifyAssertionFailure(message);
                }
                else
                {
                    IO.Debug.WriteLine($"<ScheduleDebug> {message}");
                    this.Detach();
                }
            }
        }

        /// <summary>
        /// Returns true if the current operation is executing an asynchronous state machine, else false.
        /// </summary>
        private static bool IsCurrentOperationExecutingAsynchronously()
        {
            StackTrace st = new StackTrace(false);
            bool result = false;
            for (int i = 0; i < st.FrameCount; i++)
            {
                // Traverse the stack trace to find if the current operation is executing an asynchronous state machine.
                MethodBase method = st.GetFrame(i).GetMethod();
                if (method.DeclaringType == typeof(AsyncVoidMethodBuilder) &&
                    (method.Name is "AwaitOnCompleted" || method.Name is "AwaitUnsafeOnCompleted"))
                {
                    // The operation is executing the root of an async void method, so we need to inline.
                    break;
                }
                else if (method.Name is "MoveNext" &&
                    method.DeclaringType.Namespace != typeof(CoyoteRuntime).Namespace &&
                    method.DeclaringType.Namespace != typeof(Interception.ControlledTask).Namespace &&
                    typeof(IAsyncStateMachine).IsAssignableFrom(method.DeclaringType))
                {
                    // The operation is executing the `MoveNext` of an asynchronous state machine.
                    result = true;
                    break;
                }
            }

            return result;
        }

        /// <summary>
        /// Registers a new specification monitor of the specified <see cref="Type"/>.
        /// </summary>
        internal void RegisterMonitor<T>()
            where T : Specifications.Monitor => this.DefaultActorExecutionContext.RegisterMonitor<T>();

        /// <summary>
        /// Invokes the specified monitor with the specified <see cref="Event"/>.
        /// </summary>
        internal void Monitor<T>(Event e)
            where T : Specifications.Monitor => this.DefaultActorExecutionContext.Monitor<T>(e);

        /// <summary>
        /// Checks if the assertion holds, and if not, throws an exception.
        /// </summary>
        internal void Assert(bool predicate) => this.SpecificationEngine.Assert(predicate);

        /// <summary>
        /// Checks if the assertion holds, and if not, throws an exception.
        /// </summary>
        internal void Assert(bool predicate, string s, object arg0) => this.SpecificationEngine.Assert(predicate, s, arg0);

        /// <summary>
        /// Checks if the assertion holds, and if not, throws an exception.
        /// </summary>
        internal void Assert(bool predicate, string s, object arg0, object arg1) =>
            this.SpecificationEngine.Assert(predicate, s, arg0, arg1);

        /// <summary>
        /// Checks if the assertion holds, and if not, throws an exception.
        /// </summary>
        internal void Assert(bool predicate, string s, object arg0, object arg1, object arg2) =>
            this.SpecificationEngine.Assert(predicate, s, arg0, arg1, arg2);

        /// <summary>
        /// Checks if the assertion holds, and if not, throws an exception.
        /// </summary>
        internal void Assert(bool predicate, string s, params object[] args) => this.SpecificationEngine.Assert(predicate, s, args);

        /// <summary>
        /// Creates a liveness monitor that checks if the specified task eventually completes execution successfully.
        /// </summary>
#if !DEBUG
        [DebuggerStepThrough]
#endif
        internal void MonitorTaskCompletion(Task task) => this.SpecificationEngine.MonitorTaskCompletion(task);

#if !DEBUG
        [DebuggerStepThrough]
#endif
        internal void AssertIsAwaitedTaskControlled(Task task)
        {
            if (this.SchedulingPolicy is SchedulingPolicy.Systematic &&
                !task.IsCompleted && !this.TaskMap.ContainsKey(task) &&
                !this.Configuration.IsRelaxedControlledTestingEnabled)
            {
                this.Assert(false, $"Awaiting uncontrolled task with id '{task.Id}' is not allowed: " +
                    "either mock the method that created the task, or rewrite the method's assembly.");
            }
        }

#if !DEBUG
        [DebuggerStepThrough]
#endif
        internal void AssertIsReturnedTaskControlled(Task task, string methodName)
        {
            if (!task.IsCompleted && !this.TaskMap.ContainsKey(task) &&
                !this.Configuration.IsRelaxedControlledTestingEnabled)
            {
                this.Assert(false, $"Method '{methodName}' returned an uncontrolled task with id '{task.Id}', " +
                    "which is not allowed: either mock the method, or rewrite the method's assembly.");
            }
        }

        /// <summary>
        /// Checks if the execution has deadlocked. This happens when there are no more enabled operations,
        /// but there is one or more blocked operations that are waiting some resource to complete.
        /// </summary>
#if !DEBUG
        [DebuggerHidden]
#endif
        private void CheckIfExecutionHasDeadlocked(IEnumerable<AsyncOperation> ops)
        {
            var blockedOnReceiveOperations = ops.Where(op => op.Status is AsyncOperationStatus.BlockedOnReceive).ToList();
            var blockedOnWaitOperations = ops.Where(op => op.Status is AsyncOperationStatus.BlockedOnWaitAll ||
                op.Status is AsyncOperationStatus.BlockedOnWaitAny).ToList();
            var blockedOnResources = ops.Where(op => op.Status is AsyncOperationStatus.BlockedOnResource).ToList();
            if (blockedOnReceiveOperations.Count is 0 &&
                blockedOnWaitOperations.Count is 0 &&
                blockedOnResources.Count is 0)
            {
                return;
            }

            var msg = new StringBuilder("Deadlock detected.");
            if (blockedOnReceiveOperations.Count > 0)
            {
                for (int idx = 0; idx < blockedOnReceiveOperations.Count; idx++)
                {
                    msg.Append(string.Format(CultureInfo.InvariantCulture, " {0}", blockedOnReceiveOperations[idx].Name));
                    if (idx == blockedOnReceiveOperations.Count - 2)
                    {
                        msg.Append(" and");
                    }
                    else if (idx < blockedOnReceiveOperations.Count - 1)
                    {
                        msg.Append(',');
                    }
                }

                msg.Append(blockedOnReceiveOperations.Count is 1 ? " is " : " are ");
                msg.Append("waiting to receive an event, but no other controlled tasks are enabled.");
            }

            if (blockedOnWaitOperations.Count > 0)
            {
                for (int idx = 0; idx < blockedOnWaitOperations.Count; idx++)
                {
                    msg.Append(string.Format(CultureInfo.InvariantCulture, " {0}", blockedOnWaitOperations[idx].Name));
                    if (idx == blockedOnWaitOperations.Count - 2)
                    {
                        msg.Append(" and");
                    }
                    else if (idx < blockedOnWaitOperations.Count - 1)
                    {
                        msg.Append(',');
                    }
                }

                msg.Append(blockedOnWaitOperations.Count is 1 ? " is " : " are ");
                msg.Append("waiting for a task to complete, but no other controlled tasks are enabled.");
            }

            if (blockedOnResources.Count > 0)
            {
                for (int idx = 0; idx < blockedOnResources.Count; idx++)
                {
                    msg.Append(string.Format(CultureInfo.InvariantCulture, " {0}", blockedOnResources[idx].Name));
                    if (idx == blockedOnResources.Count - 2)
                    {
                        msg.Append(" and");
                    }
                    else if (idx < blockedOnResources.Count - 1)
                    {
                        msg.Append(',');
                    }
                }

                msg.Append(blockedOnResources.Count is 1 ? " is " : " are ");
                msg.Append("waiting to acquire a resource that is already acquired, ");
                msg.Append("but no other controlled tasks are enabled.");
            }

            this.NotifyAssertionFailure(msg.ToString());
        }

        /// <summary>
        /// Checks if the execution has deadlocked. This checks occurs periodically.
        /// </summary>
        private void CheckIfExecutionHasDeadlocked(object state)
        {
            lock (this.SyncObject)
            {
                if (!this.IsAttached)
                {
                    return;
                }

                SchedulingActivityInfo info = state as SchedulingActivityInfo;
                if (info.StepCount == this.Scheduler.StepCount)
                {
                    string msg = "Potential deadlock detected. If you think this is not a deadlock, you can try " +
                        "increase the dealock detection timeout (--deadlock-timeout).";
                    this.NotifyAssertionFailure(msg, true, false, false);
                }
                else
                {
                    info.StepCount = this.Scheduler.StepCount;

                    try
                    {
                        // Start the next timeout period.
                        this.DeadlockMonitor.Change(TimeSpan.FromMilliseconds(this.Configuration.DeadlockTimeout),
                            Timeout.InfiniteTimeSpan);
                    }
                    catch (ObjectDisposedException)
                    {
                        // Benign race condition while disposing the timer.
                    }
                }
            }
        }

        /// <summary>
        /// Checks for liveness errors.
        /// </summary>
#if !DEBUG
        [DebuggerHidden]
#endif
        internal void CheckLivenessErrors()
        {
            if (this.HasFullyExploredSchedule)
            {
                this.SpecificationEngine.CheckLivenessErrors();
            }
        }

        /// <summary>
        /// Notify that an exception was not handled.
        /// </summary>
        internal void NotifyUnhandledException(Exception ex, string message, bool cancelExecution = true)
        {
            lock (this.SyncObject)
            {
                if (this.UnhandledException is null)
                {
                    this.UnhandledException = ex;
                }

                this.NotifyAssertionFailure(message, killTasks: true, cancelExecution);
            }
        }

        /// <summary>
        /// Notify that an assertion has failed.
        /// </summary>
#if !DEBUG
        [DebuggerHidden]
#endif
        internal void NotifyAssertionFailure(string text, bool killTasks = true, bool cancelExecution = true, bool logStackTrace = true)
        {
            lock (this.SyncObject)
            {
                if (!this.IsBugFound)
                {
                    this.BugReport = text;

                    this.LogWriter.LogAssertionFailure($"<ErrorLog> {text}");
                    if (logStackTrace)
                    {
                        this.LogWriter.LogAssertionFailure(string.Format("<StackTrace> {0}", ConstructStackTrace()));
                    }

                    this.RaiseOnFailureEvent(new AssertionFailureException(text));
                    this.LogWriter.LogStrategyDescription(this.Configuration.SchedulingStrategy,
                        this.Scheduler.GetDescription());

                    this.IsBugFound = true;

                    if (this.Configuration.AttachDebugger)
                    {
                        Debugger.Break();
                    }
                }

                if (killTasks)
                {
                    this.Detach(cancelExecution);
                }
            }
        }

        /// <summary>
        /// Checks if the currently executing operation is controlled by the runtime.
        /// </summary>
#if !DEBUG
        [DebuggerHidden]
#endif
        private static void ThrowUncontrolledTaskException()
        {
            // TODO: figure out if there is a way to get more information about the creator of the
            // uncontrolled task to ease the user debugging experience.
            // Report the invalid operation and then throw it to fail the uncontrolled task.
            // This will most likely crash the program, but we try to fail as cleanly and fast as possible.
            string uncontrolledTask = Task.CurrentId.HasValue ? Task.CurrentId.Value.ToString() : "<unknown>";
            throw new InvalidOperationException($"Uncontrolled task with id '{uncontrolledTask}' was detected, " +
                "which is not allowed as it can lead to race conditions or deadlocks: either mock the creator " +
                "of the uncontrolled task, or rewrite its assembly.");
        }

        /// <summary>
        /// Processes an unhandled exception in the specified asynchronous operation.
        /// </summary>
        private void ProcessUnhandledExceptionInOperation(AsyncOperation op, Exception ex)
        {
            string message = null;
            Exception exception = UnwrapException(ex);
            if (exception is ExecutionCanceledException ece)
            {
                IO.Debug.WriteLine("<Exception> {0} was thrown from operation '{1}'.",
                    ece.GetType().Name, op.Name);
                if (this.IsAttached)
                {
                    // TODO: add some tests for this, so that we check that a task (or lock) that
                    // was cached and reused from prior iteration indeed cannot cause the runtime
                    // to hang anymore.
                    message = string.Format(CultureInfo.InvariantCulture, $"Unhandled exception. {ece}");
                }
            }
            else if (exception is TaskSchedulerException)
            {
                IO.Debug.WriteLine("<Exception> {0} was thrown from operation '{1}'.",
                    exception.GetType().Name, op.Name);
            }
            else if (exception is ObjectDisposedException)
            {
                IO.Debug.WriteLine("<Exception> {0} was thrown from operation '{1}' with reason '{2}'.",
                    exception.GetType().Name, op.Name, ex.Message);
            }
            else
            {
                message = string.Format(CultureInfo.InvariantCulture, $"Unhandled exception. {exception}");
            }

            if (message != null)
            {
                // Report the unhandled exception.
                this.NotifyUnhandledException(exception, message, cancelExecution: false);
            }
        }

        /// <summary>
        /// Unwraps the specified exception.
        /// </summary>
        private static Exception UnwrapException(Exception ex)
        {
            Exception exception = ex;
            while (exception is TargetInvocationException)
            {
                exception = exception.InnerException;
            }

            if (exception is AggregateException)
            {
                exception = exception.InnerException;
            }

            return exception;
        }

        /// <summary>
        /// Reports the specified thrown exception.
        /// </summary>
        private void ReportThrownException(Exception exception)
        {
            if (!(exception is ExecutionCanceledException || exception is TaskCanceledException ||
                exception is OperationCanceledException))
            {
                string message = string.Format(CultureInfo.InvariantCulture,
                    $"Exception '{exception.GetType()}' was thrown in task '{Task.CurrentId}', " +
                    $"'{exception.Source}':\n" +
                    $"   {exception.Message}\n" +
                    $"The stack trace is:\n{exception.StackTrace}");
                this.Logger.WriteLine(LogSeverity.Warning, $"<ExceptionLog> {message}");
            }
        }

        /// <summary>
        /// Raises the <see cref="OnFailure"/> event with the specified <see cref="Exception"/>.
        /// </summary>
        internal void RaiseOnFailureEvent(Exception exception)
        {
            if (this.SchedulingPolicy != SchedulingPolicy.None &&
                (exception is ExecutionCanceledException ||
                (exception is ActionExceptionFilterException ae && ae.InnerException is ExecutionCanceledException)))
            {
                // Internal exception used during testing.
                return;
            }

            if (this.Configuration.AttachDebugger)
            {
                Debugger.Break();
                this.Configuration.AttachDebugger = false;
            }

            this.OnFailure?.Invoke(exception);
        }

        /// <summary>
        /// If scheduler is detached, throw exception to force terminate the caller.
        /// </summary>
        private void ThrowExecutionCanceledExceptionIfDetached()
        {
            if (!this.IsAttached)
            {
                throw new ExecutionCanceledException();
            }
        }

        /// <summary>
        /// Returns the stack trace without internal namespaces.
        /// </summary>
        private static string ConstructStackTrace()
        {
            StringBuilder sb = new StringBuilder();
            string[] lines = new StackTrace().ToString().Split(new[] { Environment.NewLine }, StringSplitOptions.None);
            foreach (var line in lines)
            {
                if (!line.Contains("at Microsoft.Coyote.Interception") &&
                    !line.Contains("at Microsoft.Coyote.Runtime") &&
                    !line.Contains("at Microsoft.Coyote.Testing"))
                {
                    sb.AppendLine(line);
                }
            }

            return sb.ToString();
        }

        /// <summary>
        /// Returns scheduling statistics and results.
        /// </summary>
        internal void GetSchedulingStatisticsAndResults(out bool isBugFound, out string bugReport, out int steps,
            out bool isMaxStepsReached, out bool isScheduleFair, out Exception unhandledException)
        {
            lock (this.SyncObject)
            {
                steps = this.Scheduler.StepCount;
                isMaxStepsReached = this.Scheduler.IsMaxStepsReached;
                isScheduleFair = this.Scheduler.IsScheduleFair;
                isBugFound = this.IsBugFound;
                bugReport = this.BugReport;
                unhandledException = this.UnhandledException;
            }
        }

        /// <summary>
        /// Assigns the specified runtime as the default for the current asynchronous control flow.
        /// </summary>
        internal static void AssignAsyncControlFlowRuntime(CoyoteRuntime runtime) => AsyncLocalInstance.Value = runtime;

        /// <summary>
        /// Invoked each time the <see cref="ExecutingOperation"/> async local value changes.
        /// </summary>
        private static void OnAsyncLocalExecutingOperationValueChanged(AsyncLocalValueChangedArgs<AsyncOperation> args)
        {
            if (args.ThreadContextChanged && args.PreviousValue != null && args.CurrentValue != null)
            {
                // Restore the value if it changed due to a change in the thread context,
                // but the previous and current value where not null.
                ExecutingOperation.Value = args.PreviousValue;
            }
        }

        /// <summary>
        /// Waits until the execution terminates.
        /// </summary>
        [DebuggerStepThrough]
        internal async Task WaitAsync()
        {
            await this.CompletionSource.Task;
            this.IsRunning = false;
        }

        /// <summary>
        /// Forces the scheduler to terminate.
        /// </summary>
        public void ForceStop() => this.IsRunning = false;

        /// <summary>
        /// Detaches the scheduler releasing all controlled operations.
        /// </summary>
        private void Detach(bool cancelExecution = true)
        {
            if (this.IsAttached)
            {
                this.IsAttached = false;
            }

            // Cancel any remaining operations at the end of the schedule.
            foreach (var op in this.OperationMap.Values)
            {
                if (op.Status != AsyncOperationStatus.Completed)
                {
                    op.Status = AsyncOperationStatus.Canceled;
                }
            }

            SyncMonitor.PulseAll(this.SyncObject);

            // Check if the completion source is completed, else set its result.
            if (!this.CompletionSource.Task.IsCompleted)
            {
                this.CompletionSource.SetResult(true);
            }

            if (cancelExecution)
            {
                // Throw exception to force terminate the current operation.
                throw new ExecutionCanceledException();
            }
        }

        /// <summary>
        /// Disposes runtime resources.
        /// </summary>
        private void Dispose(bool disposing)
        {
            if (disposing)
            {
                this.OperationIdCounter = 0;
                this.DefaultActorExecutionContext.Dispose();
                this.SpecificationEngine.Dispose();

                if (this.SchedulingPolicy is SchedulingPolicy.Systematic)
                {
                    // Note: this makes it possible to run a Controlled unit test followed by a production
                    // unit test, whereas before that would throw "Uncontrolled Task" exceptions.
                    // This does not solve mixing unit test type in parallel.
                    Interlocked.Decrement(ref ExecutionControlledUseCount);
                }
                else if (this.SchedulingPolicy is SchedulingPolicy.Fuzzing)
                {
                    this.DeadlockMonitor.Dispose();
                }

                AssignAsyncControlFlowRuntime(null);
            }
        }

        /// <summary>
        /// Disposes runtime resources.
        /// </summary>
        public void Dispose()
        {
            this.Dispose(true);
            GC.SuppressFinalize(this);
        }
    }
}
