﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Coyote.Runtime;

namespace Microsoft.Coyote.Testing.Fuzzing
{
    /// <summary>
    /// Scheduler that fuzzes the execution of operations during systematic testing.
    /// </summary>
    /// <remarks>
    /// Invoking the scheduler is thread-safe.
    /// </remarks>
#if !DEBUG
    [DebuggerStepThrough]
#endif
    internal sealed class FuzzingScheduler
    {
        /// <summary>
        /// Provides access to the operation executing on each asynchronous control flow.
        /// </summary>
        private static readonly AsyncLocal<AsyncOperation> ExecutingOperation =
            new AsyncLocal<AsyncOperation>(OnAsyncLocalExecutingOperationValueChanged);

        /// <summary>
        /// The configuration used by the scheduler.
        /// </summary>
        private readonly Configuration Configuration;

        /// <summary>
        /// Responsible for controlling the program execution during systematic testing.
        /// </summary>
        private readonly CoyoteRuntime Runtime;

        /// <summary>
        /// Map from unique ids to asynchronous operations.
        /// </summary>
        private readonly Dictionary<ulong, AsyncOperation> OperationMap;

        /// <summary>
        /// Object that is used to synchronize access to the scheduler.
        /// </summary>
        internal readonly object SyncObject;

        /// <summary>
        /// The scheduler completion source.
        /// </summary>
        private readonly TaskCompletionSource<bool> CompletionSource;

        /// <summary>
        /// True if the user program is executing, else false.
        /// </summary>
        internal volatile bool IsProgramExecuting;

        /// <summary>
        /// Number of scheduled steps.
        /// </summary>
        internal int ScheduledSteps { get; private set; }

        /// <summary>
        /// True if the max scheduling steps bound has been reached in the current execution.
        /// </summary>
        private bool IsMaxScheduledStepsBoundReached
        {
            get
            {
                if (this.Configuration.MaxFairSchedulingSteps is 0)
                {
                    return false;
                }

                return this.ScheduledSteps >= this.Configuration.MaxFairSchedulingSteps;
            }
        }

        /// <summary>
        /// Checks if the schedule has been fully explored.
        /// </summary>
        internal bool HasFullyExploredSchedule { get; private set; }

        /// <summary>
        /// True if a bug was found, else false.
        /// </summary>
        internal bool IsBugFound { get; private set; }

        /// <summary>
        /// Bug report.
        /// </summary>
        internal string BugReport { get; private set; }

        /// <summary>
        /// Associated with the bug report is an optional unhandled exception.
        /// </summary>
        internal Exception UnhandledException { get; private set; }

        /// <summary>
        /// Timer implementing an activity monitor.
        /// </summary>
        private readonly Timer ActivityMonitor;

        /// <summary>
        /// The amount of time to wait before checking for activity.
        /// </summary>
        public readonly TimeSpan ActivityCheckDueTime;

        /// <summary>
        /// The time interval between checking for activity.
        /// </summary>
        public readonly TimeSpan ActivityCheckPeriod;

        /// <summary>
        /// True if the scheduler is attached to the executing program, else false.
        /// </summary>
        internal bool IsAttached { get; private set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="FuzzingScheduler"/> class.
        /// </summary>
        internal FuzzingScheduler(CoyoteRuntime runtime, Configuration configuration)
        {
            this.Configuration = configuration;
            this.Runtime = runtime;
            this.OperationMap = new Dictionary<ulong, AsyncOperation>();
            this.SyncObject = new object();
            this.CompletionSource = new TaskCompletionSource<bool>();
            this.IsProgramExecuting = true;
            this.IsAttached = true;
            this.IsBugFound = false;
            this.ScheduledSteps = 0;
            this.HasFullyExploredSchedule = false;
            this.ActivityMonitor = new Timer(this.CheckActivity, new ActivityInfo(),
                Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
            this.ActivityCheckDueTime = TimeSpan.FromMilliseconds(100);
            this.ActivityCheckPeriod = TimeSpan.FromMilliseconds(100);
        }

        /// <summary>
        /// Starts the execution of the specified operation.
        /// </summary>
        /// <param name="op">The operation to start executing.</param>
        internal void StartOperation(AsyncOperation op)
        {
            lock (this.SyncObject)
            {
                this.ThrowExecutionCanceledExceptionIfDetached();

                IO.Debug.WriteLine($"<ScheduleDebug> Starting the operation of '{0}' on task '{1}'.", op.Name, Task.CurrentId);
                ExecutingOperation.Value = op;

#if NETSTANDARD2_0
                if (!this.OperationMap.ContainsKey(op.Id))
                {
                    this.OperationMap.Add(op.Id, op);
                }
#else
                this.OperationMap.TryAdd(op.Id, op);
#endif

                if (this.OperationMap.Count is 1)
                {
                    this.ActivityMonitor.Change(this.ActivityCheckDueTime, Timeout.InfiniteTimeSpan);
                }
            }
        }

        /// <summary>
        /// Completes the specified operation.
        /// </summary>
        /// <param name="op">The operation to complete.</param>
        internal void CompleteOperation(AsyncOperation op)
        {
            lock (this.SyncObject)
            {
                IO.Debug.WriteLine("<ScheduleDebug> Completed the operation of '{0}' on task '{1}'.", op.Name, Task.CurrentId);
            }
        }

        /// <summary>
        /// Injects a delay in the currently executing operation.
        /// </summary>
        internal void InjectDelay()
        {
            lock (this.SyncObject)
            {
                this.ThrowExecutionCanceledExceptionIfDetached();

                int delay = 0;
                IO.Debug.WriteLine("<ScheduleDebug> Injecting delay of {0}ms on task '{1}'.", delay, Task.CurrentId);
                Thread.Sleep(delay);

                this.ScheduledSteps++;
                this.CheckIfSchedulingStepsBoundIsReached();
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
        /// Checks if the scheduling steps bound has been reached. If yes,
        /// it stops the scheduler and kills all enabled operations.
        /// </summary>
#if !DEBUG
        [DebuggerHidden]
#endif
        private void CheckIfSchedulingStepsBoundIsReached()
        {
            lock (this.SyncObject)
            {
                if (this.Configuration.MaxFairSchedulingSteps is 0)
                {
                }
                else if (this.ScheduledSteps >= this.Configuration.MaxFairSchedulingSteps)
                {
                    string message = $"Scheduling steps bound of {this.Configuration.MaxFairSchedulingSteps} reached.";
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
        }

        internal void NotifyUnhandledException(Exception ex, string message)
        {
            lock (this.SyncObject)
            {
                if (this.UnhandledException is null)
                {
                    this.UnhandledException = ex;
                }

                this.NotifyAssertionFailure(message, killTasks: true, cancelExecution: false);
            }
        }

        /// <summary>
        /// Notify that an assertion has failed.
        /// </summary>
#if !DEBUG
        [DebuggerHidden]
#endif
        internal void NotifyAssertionFailure(string text, bool killTasks = true, bool cancelExecution = true)
        {
            lock (this.SyncObject)
            {
                if (!this.IsBugFound)
                {
                    this.BugReport = text;

                    this.Runtime.LogWriter.LogAssertionFailure($"<ErrorLog> {text}");
                    this.Runtime.LogWriter.LogAssertionFailure(string.Format("<StackTrace> {0}", ConstructStackTrace()));
                    this.Runtime.RaiseOnFailureEvent(new AssertionFailureException(text));
                    this.Runtime.LogWriter.LogStrategyDescription("Fuzzing", string.Empty);

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
        /// Returns scheduling statistics and results.
        /// </summary>
        internal void GetSchedulingStatisticsAndResults(out bool isBugFound, out string bugReport, out int scheduledSteps,
            out bool isMaxScheduledStepsBoundReached, out bool isScheduleFair, out Exception unhandledException)
        {
            lock (this.SyncObject)
            {
                scheduledSteps = this.ScheduledSteps;
                isMaxScheduledStepsBoundReached = this.IsMaxScheduledStepsBoundReached;
                isScheduleFair = true;
                isBugFound = this.IsBugFound;
                bugReport = this.BugReport;
                unhandledException = this.UnhandledException;
            }
        }

        private static string ConstructStackTrace()
        {
            StringBuilder sb = new StringBuilder();
            string[] lines = new StackTrace().ToString().Split(new[] { Environment.NewLine }, StringSplitOptions.None);
            foreach (var line in lines)
            {
                if (!line.Contains("at Microsoft.Coyote.SystematicTesting"))
                {
                    sb.AppendLine(line);
                }
            }

            return sb.ToString();
        }

        /// <summary>
        /// Checks for activity on each timeout.
        /// </summary>
        private void CheckActivity(object state)
        {
            lock (this.SyncObject)
            {
                if (!this.IsAttached)
                {
                    return;
                }

                ActivityInfo info = state as ActivityInfo;
                if (info.OperationCount == this.OperationMap.Count &&
                    info.StepCount == this.ScheduledSteps)
                {
                    IO.Debug.WriteLine("<ScheduleDebug> Schedule explored.");
                    this.HasFullyExploredSchedule = true;
                    this.Detach(false);
                }
                else
                {
                    info.OperationCount = this.OperationMap.Count;
                    info.StepCount = this.ScheduledSteps;

                    try
                    {
                        // Start the next timeout period.
                        this.ActivityMonitor.Change(this.ActivityCheckPeriod, Timeout.InfiniteTimeSpan);
                    }
                    catch (ObjectDisposedException)
                    {
                        // Benign race condition while disposing the timer.
                    }
                }
            }
        }

        /// <summary>
        /// Waits until the scheduler terminates.
        /// </summary>
        internal async Task WaitAsync()
        {
            await this.CompletionSource.Task;
            this.IsProgramExecuting = false;
        }

        /// <summary>
        /// Detaches the scheduler releasing all controlled operations.
        /// </summary>
        private void Detach(bool cancelExecution = true)
        {
            if (!this.IsAttached)
            {
                this.IsAttached = false;
                this.ActivityMonitor.Dispose();
            }

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
        /// Forces the scheduler to terminate.
        /// </summary>
        internal void ForceStop() => this.IsProgramExecuting = false;

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
        /// Defines activity information that is used to check for termination.
        /// </summary>
        private class ActivityInfo
        {
            /// <summary>
            /// Number of created operations since last activity check.
            /// </summary>
            public int OperationCount { get; set; }

            /// <summary>
            /// Number of scheduling steps since last activity check.
            /// </summary>
            public int StepCount { get; set; }
        }
    }
}