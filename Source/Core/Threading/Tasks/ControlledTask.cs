﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Coyote.Runtime;

namespace Microsoft.Coyote.Threading.Tasks
{
    /// <summary>
    /// Represents an asynchronous operation. Each <see cref="ControlledTask"/> is a thin wrapper
    /// over <see cref="Task"/> and each call simply invokes the wrapped task. During testing, a
    /// <see cref="ControlledTask"/> is controlled by the runtime and systematically interleaved
    /// with other asynchronous operations to find bugs.
    /// </summary>
    [AsyncMethodBuilder(typeof(AsyncControlledTaskMethodBuilder))]
    public class ControlledTask : IDisposable
    {
        /// <summary>
        /// A <see cref="ControlledTask"/> that has completed successfully.
        /// </summary>
        public static ControlledTask CompletedTask { get; } = new ControlledTask(Task.CompletedTask);

        /// <summary>
        /// Returns the id of the currently executing <see cref="ControlledTask"/>.
        /// </summary>
        public static int? CurrentId => CoyoteRuntime.Provider.Current.CurrentTaskId;

        /// <summary>
        /// Internal task used to execute the work.
        /// </summary>
        private protected readonly Task InternalTask;

        /// <summary>
        /// The id of this task.
        /// </summary>
        public int Id => this.InternalTask.Id;

        /// <summary>
        /// Task that provides access to the completed work.
        /// </summary>
        internal Task AwaiterTask => this.InternalTask;

        /// <summary>
        /// Value that indicates whether the task has completed.
        /// </summary>
        public bool IsCompleted => this.InternalTask.IsCompleted;

        /// <summary>
        /// Value that indicates whether the task completed execution due to being canceled.
        /// </summary>
        public bool IsCanceled => this.InternalTask.IsCanceled;

        /// <summary>
        /// Value that indicates whether the task completed due to an unhandled exception.
        /// </summary>
        public bool IsFaulted => this.InternalTask.IsFaulted;

        /// <summary>
        /// Gets the <see cref="System.AggregateException"/> that caused the task
        /// to end prematurely. If the task completed successfully or has not yet
        /// thrown any exceptions, this will return null.
        /// </summary>
        public AggregateException Exception => this.InternalTask.Exception;

        /// <summary>
        /// The status of this task.
        /// </summary>
        public TaskStatus Status => this.InternalTask.Status;

        /// <summary>
        /// Initializes a new instance of the <see cref="ControlledTask"/> class.
        /// </summary>
        [DebuggerStepThrough]
        internal ControlledTask(Task task)
        {
            this.InternalTask = task ?? throw new ArgumentNullException(nameof(task));
        }

        /// <summary>
        /// Creates a <see cref="ControlledTask{TResult}"/> that is completed successfully with the specified result.
        /// </summary>
        /// <typeparam name="TResult">The type of the result returned by the task.</typeparam>
        /// <param name="result">The result to store into the completed task.</param>
        /// <returns>The successfully completed task.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ControlledTask<TResult> FromResult<TResult>(TResult result) =>
            new ControlledTask<TResult>(Task.FromResult(result));

        /// <summary>
        /// Creates a <see cref="ControlledTask"/> that is completed due to
        /// cancellation with a specified cancellation token.
        /// </summary>
        /// <param name="cancellationToken">The cancellation token with which to complete the task.</param>
        /// <returns>The canceled task.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ControlledTask FromCanceled(CancellationToken cancellationToken) =>
            new ControlledTask(Task.FromCanceled(cancellationToken));

        /// <summary>
        /// Creates a <see cref="ControlledTask{TResult}"/> that is completed due to
        /// cancellation with a specified cancellation token.
        /// </summary>
        /// <typeparam name="TResult">The type of the result returned by the task.</typeparam>
        /// <param name="cancellationToken">The cancellation token with which to complete the task.</param>
        /// <returns>The canceled task.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ControlledTask<TResult> FromCanceled<TResult>(CancellationToken cancellationToken) =>
            new ControlledTask<TResult>(Task.FromCanceled<TResult>(cancellationToken));

        /// <summary>
        /// Creates a <see cref="ControlledTask"/> that is completed with a specified exception.
        /// </summary>
        /// <param name="exception">The exception with which to complete the task.</param>
        /// <returns>The faulted task.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ControlledTask FromException(Exception exception) =>
            new ControlledTask(Task.FromException(exception));

        /// <summary>
        /// Creates a <see cref="ControlledTask{TResult}"/> that is completed with a specified exception.
        /// </summary>
        /// <typeparam name="TResult">The type of the result returned by the task.</typeparam>
        /// <param name="exception">The exception with which to complete the task.</param>
        /// <returns>The faulted task.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ControlledTask<TResult> FromException<TResult>(Exception exception) =>
            new ControlledTask<TResult>(Task.FromException<TResult>(exception));

        /// <summary>
        /// Queues the specified work to run on the thread pool and returns a <see cref="ControlledTask"/>
        /// object that represents that work. A cancellation token allows the work to be cancelled.
        /// </summary>
        /// <param name="action">The work to execute asynchronously.</param>
        /// <returns>Task that represents the work to run asynchronously.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ControlledTask Run(Action action) =>
            CoyoteRuntime.Provider.Current.CreateControlledTask(action, default);

        /// <summary>
        /// Queues the specified work to run on the thread pool and returns a <see cref="ControlledTask"/>
        /// object that represents that work.
        /// </summary>
        /// <param name="action">The work to execute asynchronously.</param>
        /// <param name="cancellationToken">Cancellation token that can be used to cancel the work.</param>
        /// <returns>Task that represents the work to run asynchronously.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ControlledTask Run(Action action, CancellationToken cancellationToken) =>
            CoyoteRuntime.Provider.Current.CreateControlledTask(action, cancellationToken);

        /// <summary>
        /// Queues the specified work to run on the thread pool and returns a <see cref="ControlledTask"/>
        /// object that represents that work.
        /// </summary>
        /// <param name="function">The work to execute asynchronously.</param>
        /// <returns>Task that represents the work to run asynchronously.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ControlledTask Run(Func<ControlledTask> function) =>
            CoyoteRuntime.Provider.Current.CreateControlledTask(function, default);

        /// <summary>
        /// Queues the specified work to run on the thread pool and returns a <see cref="ControlledTask"/>
        /// object that represents that work. A cancellation token allows the work to be cancelled.
        /// </summary>
        /// <param name="function">The work to execute asynchronously.</param>
        /// <param name="cancellationToken">Cancellation token that can be used to cancel the work.</param>
        /// <returns>Task that represents the work to run asynchronously.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ControlledTask Run(Func<ControlledTask> function, CancellationToken cancellationToken) =>
            CoyoteRuntime.Provider.Current.CreateControlledTask(function, cancellationToken);

        /// <summary>
        /// Queues the specified work to run on the thread pool and returns a <see cref="ControlledTask"/>
        /// object that represents that work.
        /// </summary>
        /// <typeparam name="TResult">The result type of the task.</typeparam>
        /// <param name="function">The work to execute asynchronously.</param>
        /// <returns>Task that represents the work to run asynchronously.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ControlledTask<TResult> Run<TResult>(Func<TResult> function) =>
            CoyoteRuntime.Provider.Current.CreateControlledTask(function, default);

        /// <summary>
        /// Queues the specified work to run on the thread pool and returns a <see cref="ControlledTask"/>
        /// object that represents that work. A cancellation token allows the work to be cancelled.
        /// </summary>
        /// <typeparam name="TResult">The result type of the task.</typeparam>
        /// <param name="function">The work to execute asynchronously.</param>
        /// <param name="cancellationToken">Cancellation token that can be used to cancel the work.</param>
        /// <returns>Task that represents the work to run asynchronously.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ControlledTask<TResult> Run<TResult>(Func<TResult> function, CancellationToken cancellationToken) =>
            CoyoteRuntime.Provider.Current.CreateControlledTask(function, cancellationToken);

        /// <summary>
        /// Queues the specified work to run on the thread pool and returns a <see cref="ControlledTask"/>
        /// object that represents that work.
        /// </summary>
        /// <typeparam name="TResult">The result type of the task.</typeparam>
        /// <param name="function">The work to execute asynchronously.</param>
        /// <returns>Task that represents the work to run asynchronously.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ControlledTask<TResult> Run<TResult>(Func<ControlledTask<TResult>> function) =>
            CoyoteRuntime.Provider.Current.CreateControlledTask(function, default);

        /// <summary>
        /// Queues the specified work to run on the thread pool and returns a <see cref="ControlledTask"/>
        /// object that represents that work. A cancellation token allows the work to be cancelled.
        /// </summary>
        /// <typeparam name="TResult">The result type of the task.</typeparam>
        /// <param name="function">The work to execute asynchronously.</param>
        /// <param name="cancellationToken">Cancellation token that can be used to cancel the work.</param>
        /// <returns>Task that represents the work to run asynchronously.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ControlledTask<TResult> Run<TResult>(Func<ControlledTask<TResult>> function, CancellationToken cancellationToken) =>
            CoyoteRuntime.Provider.Current.CreateControlledTask(function, cancellationToken);

        /// <summary>
        /// Creates a <see cref="ControlledTask"/> that completes after a time delay.
        /// </summary>
        /// <param name="millisecondsDelay">
        /// The number of milliseconds to wait before completing the returned task, or -1 to wait indefinitely.
        /// </param>
        /// <returns>Task that represents the time delay.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ControlledTask Delay(int millisecondsDelay) =>
            CoyoteRuntime.Provider.Current.CreateControlledTaskDelay(millisecondsDelay, default);

        /// <summary>
        /// Creates a <see cref="ControlledTask"/> that completes after a time delay.
        /// </summary>
        /// <param name="millisecondsDelay">
        /// The number of milliseconds to wait before completing the returned task, or -1 to wait indefinitely.
        /// </param>
        /// <param name="cancellationToken">Cancellation token that can be used to cancel the delay.</param>
        /// <returns>Task that represents the time delay.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ControlledTask Delay(int millisecondsDelay, CancellationToken cancellationToken) =>
            CoyoteRuntime.Provider.Current.CreateControlledTaskDelay(millisecondsDelay, cancellationToken);

        /// <summary>
        /// Creates a <see cref="ControlledTask"/> that completes after a specified time interval.
        /// </summary>
        /// <param name="delay">
        /// The time span to wait before completing the returned task, or TimeSpan.FromMilliseconds(-1)
        /// to wait indefinitely.
        /// </param>
        /// <returns>Task that represents the time delay.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ControlledTask Delay(TimeSpan delay) =>
            CoyoteRuntime.Provider.Current.CreateControlledTaskDelay(delay, default);

        /// <summary>
        /// Creates a <see cref="ControlledTask"/> that completes after a specified time interval.
        /// </summary>
        /// <param name="delay">
        /// The time span to wait before completing the returned task, or TimeSpan.FromMilliseconds(-1)
        /// to wait indefinitely.
        /// </param>
        /// <param name="cancellationToken">Cancellation token that can be used to cancel the delay.</param>
        /// <returns>Task that represents the time delay.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ControlledTask Delay(TimeSpan delay, CancellationToken cancellationToken) =>
            CoyoteRuntime.Provider.Current.CreateControlledTaskDelay(delay, cancellationToken);

        /// <summary>
        /// Creates a <see cref="ControlledTask"/> that will complete when all tasks
        /// in the specified array have completed.
        /// </summary>
        /// <param name="tasks">The tasks to wait for completion.</param>
        /// <returns>Task that represents the completion of all of the specified tasks.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ControlledTask WhenAll(params ControlledTask[] tasks) =>
            CoyoteRuntime.Provider.Current.WaitAllTasksAsync(tasks);

        /// <summary>
        /// Creates a <see cref="ControlledTask"/> that will complete when all tasks
        /// in the specified enumerable collection have completed.
        /// </summary>
        /// <param name="tasks">The tasks to wait for completion.</param>
        /// <returns>Task that represents the completion of all of the specified tasks.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ControlledTask WhenAll(IEnumerable<ControlledTask> tasks) =>
            CoyoteRuntime.Provider.Current.WaitAllTasksAsync(tasks);

        /// <summary>
        /// Creates a <see cref="ControlledTask"/> that will complete when all tasks
        /// in the specified array have completed.
        /// </summary>
        /// <typeparam name="TResult">The result type of the task.</typeparam>
        /// <param name="tasks">The tasks to wait for completion.</param>
        /// <returns>Task that represents the completion of all of the specified tasks.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ControlledTask<TResult[]> WhenAll<TResult>(params ControlledTask<TResult>[] tasks) =>
            CoyoteRuntime.Provider.Current.WaitAllTasksAsync(tasks);

        /// <summary>
        /// Creates a <see cref="ControlledTask"/> that will complete when all tasks
        /// in the specified enumerable collection have completed.
        /// </summary>
        /// <typeparam name="TResult">The result type of the task.</typeparam>
        /// <param name="tasks">The tasks to wait for completion.</param>
        /// <returns>Task that represents the completion of all of the specified tasks.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ControlledTask<TResult[]> WhenAll<TResult>(IEnumerable<ControlledTask<TResult>> tasks) =>
            CoyoteRuntime.Provider.Current.WaitAllTasksAsync(tasks);

        /// <summary>
        /// Creates a <see cref="ControlledTask"/> that will complete when any task
        /// in the specified array have completed.
        /// </summary>
        /// <param name="tasks">The tasks to wait for completion.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ControlledTask<ControlledTask> WhenAny(params ControlledTask[] tasks) =>
            CoyoteRuntime.Provider.Current.WaitAnyTaskAsync(tasks);

        /// <summary>
        /// Creates a <see cref="ControlledTask"/> that will complete when any task
        /// in the specified enumerable collection have completed.
        /// </summary>
        /// <param name="tasks">The tasks to wait for completion.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ControlledTask<ControlledTask> WhenAny(IEnumerable<ControlledTask> tasks) =>
            CoyoteRuntime.Provider.Current.WaitAnyTaskAsync(tasks);

        /// <summary>
        /// Creates a <see cref="ControlledTask"/> that will complete when any task
        /// in the specified array have completed.
        /// </summary>
        /// <param name="tasks">The tasks to wait for completion.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ControlledTask<ControlledTask<TResult>> WhenAny<TResult>(params ControlledTask<TResult>[] tasks) =>
            CoyoteRuntime.Provider.Current.WaitAnyTaskAsync(tasks);

        /// <summary>
        /// Creates a <see cref="ControlledTask"/> that will complete when any task
        /// in the specified enumerable collection have completed.
        /// </summary>
        /// <param name="tasks">The tasks to wait for completion.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ControlledTask<ControlledTask<TResult>> WhenAny<TResult>(IEnumerable<ControlledTask<TResult>> tasks) =>
            CoyoteRuntime.Provider.Current.WaitAnyTaskAsync(tasks);

        /// <summary>
        /// Waits for all of the provided <see cref="ControlledTask"/> objects to complete execution.
        /// </summary>
        /// <param name="tasks">The tasks to wait for completion.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void WaitAll(params ControlledTask[] tasks) =>
            CoyoteRuntime.Provider.Current.WaitAllTasks(tasks);

        /// <summary>
        /// Waits for all of the provided <see cref="ControlledTask"/> objects to complete
        /// execution within a specified number of milliseconds.
        /// </summary>
        /// <param name="tasks">The tasks to wait for completion.</param>
        /// <param name="millisecondsTimeout">The number of milliseconds to wait, or -1 to wait indefinitely.</param>
        /// <returns>True if all tasks completed execution within the allotted time; otherwise, false.</returns>
        public static bool WaitAll(ControlledTask[] tasks, int millisecondsTimeout) =>
            CoyoteRuntime.Provider.Current.WaitAllTasks(tasks, millisecondsTimeout);

        /// <summary>
        /// Waits for any of the provided <see cref="ControlledTask"/> objects to complete
        /// execution within a specified number of milliseconds or until a cancellation
        /// token is cancelled.
        /// </summary>
        /// <param name="tasks">The tasks to wait for completion.</param>
        /// <param name="millisecondsTimeout">The number of milliseconds to wait, or -1 to wait indefinitely.</param>
        /// <param name="cancellationToken">Cancellation token that can be used to cancel the wait.</param>
        /// <returns>True if all tasks completed execution within the allotted time; otherwise, false.</returns>
        public static bool WaitAll(ControlledTask[] tasks, int millisecondsTimeout, CancellationToken cancellationToken) =>
            CoyoteRuntime.Provider.Current.WaitAllTasks(tasks, millisecondsTimeout, cancellationToken);

        /// <summary>
        /// Waits for all of the provided <see cref="ControlledTask"/> objects to complete
        /// execution unless the wait is cancelled.
        /// </summary>
        /// <param name="tasks">The tasks to wait for completion.</param>
        /// <param name="cancellationToken">Cancellation token that can be used to cancel the wait.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void WaitAll(ControlledTask[] tasks, CancellationToken cancellationToken) =>
            CoyoteRuntime.Provider.Current.WaitAllTasks(tasks, cancellationToken);

        /// <summary>
        /// Waits for all of the provided <see cref="ControlledTask"/> objects to complete
        /// execution within a specified time interval.
        /// </summary>
        /// <param name="tasks">The tasks to wait for completion.</param>
        /// <param name="timeout">
        /// A time span that represents the number of milliseconds to wait, or
        /// TimeSpan.FromMilliseconds(-1) to wait indefinitely.
        /// </param>
        /// <returns>True if all tasks completed execution within the allotted time; otherwise, false.</returns>
        public static bool WaitAll(ControlledTask[] tasks, TimeSpan timeout) =>
            CoyoteRuntime.Provider.Current.WaitAllTasks(tasks, timeout);

        /// <summary>
        /// Waits for any of the provided <see cref="ControlledTask"/> objects to complete execution.
        /// </summary>
        /// <param name="tasks">The tasks to wait for completion.</param>
        /// <returns>The index of the completed task in the tasks array.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int WaitAny(params ControlledTask[] tasks) =>
            CoyoteRuntime.Provider.Current.WaitAnyTask(tasks);

        /// <summary>
        /// Waits for any of the provided <see cref="ControlledTask"/> objects to complete
        /// execution within a specified number of milliseconds.
        /// </summary>
        /// <param name="tasks">The tasks to wait for completion.</param>
        /// <param name="millisecondsTimeout">The number of milliseconds to wait, or -1 to wait indefinitely.</param>
        /// <returns>The index of the completed task in the tasks array, or -1 if the timeout occurred.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int WaitAny(ControlledTask[] tasks, int millisecondsTimeout) =>
            CoyoteRuntime.Provider.Current.WaitAnyTask(tasks, millisecondsTimeout);

        /// <summary>
        /// Waits for any of the provided <see cref="ControlledTask"/> objects to complete
        /// execution within a specified number of milliseconds or until a cancellation
        /// token is cancelled.
        /// </summary>
        /// <param name="tasks">The tasks to wait for completion.</param>
        /// <param name="millisecondsTimeout">The number of milliseconds to wait, or -1 to wait indefinitely.</param>
        /// <param name="cancellationToken">Cancellation token that can be used to cancel the wait.</param>
        /// <returns>The index of the completed task in the tasks array, or -1 if the timeout occurred.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int WaitAny(ControlledTask[] tasks, int millisecondsTimeout, CancellationToken cancellationToken) =>
            CoyoteRuntime.Provider.Current.WaitAnyTask(tasks, millisecondsTimeout, cancellationToken);

        /// <summary>
        /// Waits for any of the provided <see cref="ControlledTask"/> objects to complete
        /// execution unless the wait is cancelled.
        /// </summary>
        /// <param name="tasks">The tasks to wait for completion.</param>
        /// <param name="cancellationToken">Cancellation token that can be used to cancel the wait.</param>
        /// <returns>The index of the completed task in the tasks array.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int WaitAny(ControlledTask[] tasks, CancellationToken cancellationToken) =>
            CoyoteRuntime.Provider.Current.WaitAnyTask(tasks, cancellationToken);

        /// <summary>
        /// Waits for any of the provided <see cref="ControlledTask"/> objects to complete
        /// execution within a specified time interval.
        /// </summary>
        /// <param name="tasks">The tasks to wait for completion.</param>
        /// <param name="timeout">
        /// A time span that represents the number of milliseconds to wait, or
        /// TimeSpan.FromMilliseconds(-1) to wait indefinitely.
        /// </param>
        /// <returns>The index of the completed task in the tasks array, or -1 if the timeout occurred.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static int WaitAny(ControlledTask[] tasks, TimeSpan timeout) =>
            CoyoteRuntime.Provider.Current.WaitAnyTask(tasks, timeout);

        /// <summary>
        /// Creates an awaitable that asynchronously yields back to the current context when awaited.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static ControlledYieldAwaitable Yield() => default;

        /// <summary>
        /// Waits for the task to complete execution.
        /// </summary>
        public virtual void Wait() => this.InternalTask.Wait();

        /// <summary>
        /// Waits for the task to complete execution within a specified number of milliseconds.
        /// </summary>
        /// <param name="millisecondsTimeout">The number of milliseconds to wait, or -1 to wait indefinitely.</param>
        /// <returns>True if the task completed execution within the allotted time; otherwise, false.</returns>
        public virtual bool Wait(int millisecondsTimeout) => this.InternalTask.Wait(millisecondsTimeout);

        /// <summary>
        /// Waits for the task to complete execution. The wait terminates if a timeout interval
        /// elapses or a cancellation token is canceled before the task completes.
        /// </summary>
        /// <param name="millisecondsTimeout">The number of milliseconds to wait, or -1 to wait indefinitely.</param>
        /// <param name="cancellationToken">A cancellation token to observe while waiting for the task to complete.</param>
        /// <returns>True if the task completed execution within the allotted time; otherwise, false.</returns>
        public virtual bool Wait(int millisecondsTimeout, CancellationToken cancellationToken) =>
            this.InternalTask.Wait(millisecondsTimeout, cancellationToken);

        /// <summary>
        /// Waits for the task to complete execution within a specified time interval.
        /// </summary>
        /// <param name="timeout">
        /// A time span that represents the number of milliseconds to wait, or
        /// TimeSpan.FromMilliseconds(-1) to wait indefinitely.
        /// </param>
        /// <returns>True if the task completed execution within the allotted time; otherwise, false.</returns>
        public virtual bool Wait(TimeSpan timeout) => this.InternalTask.Wait(timeout);

        /// <summary>
        /// Waits for the task to complete execution. The wait terminates if
        /// a cancellation token is canceled before the task completes.
        /// </summary>
        /// <param name="cancellationToken">A cancellation token to observe while waiting for the task to complete.</param>
        public virtual void Wait(CancellationToken cancellationToken) => this.InternalTask.Wait(cancellationToken);

        /// <summary>
        /// Gets an awaiter for this awaitable.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public virtual ControlledTaskAwaiter GetAwaiter() => new ControlledTaskAwaiter(this, this.InternalTask);

        /// <summary>
        /// Ends the wait for the completion of the task.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal virtual void GetResult(TaskAwaiter awaiter) => awaiter.GetResult();

        /// <summary>
        /// Sets the action to perform when the task completes.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal virtual void OnCompleted(Action continuation, TaskAwaiter awaiter) => awaiter.OnCompleted(continuation);

        /// <summary>
        /// Schedules the continuation action that is invoked when the task completes.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal virtual void UnsafeOnCompleted(Action continuation, TaskAwaiter awaiter) =>
            awaiter.UnsafeOnCompleted(continuation);

        /// <summary>
        /// Configures an awaiter used to await this task.
        /// </summary>
        /// <param name="continueOnCapturedContext">
        /// True to attempt to marshal the continuation back to the original context captured; otherwise, false.
        /// </param>
        public virtual ConfiguredControlledTaskAwaitable ConfigureAwait(bool continueOnCapturedContext) =>
            new ConfiguredControlledTaskAwaitable(this, this.InternalTask, continueOnCapturedContext);

        /// <summary>
        /// Injects a context switch point that can be systematically explored during testing.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void ExploreContextSwitch() => CoyoteRuntime.Provider.Current.ExploreContextSwitch();

        /// <summary>
        /// Ends the wait for the completion of the task.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal virtual void GetResult(ConfiguredTaskAwaitable.ConfiguredTaskAwaiter awaiter) => awaiter.GetResult();

        /// <summary>
        /// Sets the action to perform when the task completes.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal virtual void OnCompleted(Action continuation, ConfiguredTaskAwaitable.ConfiguredTaskAwaiter awaiter) =>
            awaiter.OnCompleted(continuation);

        /// <summary>
        /// Schedules the continuation action that is invoked when the task completes.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal virtual void UnsafeOnCompleted(Action continuation, ConfiguredTaskAwaitable.ConfiguredTaskAwaiter awaiter) =>
            awaiter.UnsafeOnCompleted(continuation);

        /// <summary>
        /// Converts the specified <see cref="ControlledTask"/> into a <see cref="Task"/>.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public Task ToTask() => this.InternalTask;

        /// <summary>
        /// Disposes the <see cref="ControlledTask"/>, releasing all of its unmanaged resources.
        /// </summary>
        protected virtual void Dispose(bool disposing)
        {
        }

        /// <summary>
        /// Disposes the <see cref="ControlledTask"/>, releasing all of its unmanaged resources.
        /// </summary>
        /// <remarks>
        /// Unlike most of the members of <see cref="ControlledTask"/>, this method is not thread-safe.
        /// </remarks>
        public void Dispose()
        {
            this.InternalTask.Dispose();
            this.Dispose(true);
            GC.SuppressFinalize(this);
        }
    }

    /// <summary>
    /// Represents an asynchronous operation that can return a value. Each <see cref="ControlledTask{TResult}"/>
    /// is a thin wrapper over <see cref="Task{TResult}"/> and each call simply invokes the wrapped task. During
    /// testing, a <see cref="ControlledTask"/> is controlled by the runtime and systematically interleaved with
    /// other asynchronous operations to find bugs.
    /// </summary>
    /// <typeparam name="TResult">The type of the produced result.</typeparam>
    [AsyncMethodBuilder(typeof(AsyncControlledTaskMethodBuilder<>))]
    public class ControlledTask<TResult> : ControlledTask
    {
        /// <summary>
        /// Task that provides access to the completed work.
        /// </summary>
        internal new Task<TResult> AwaiterTask => this.InternalTask as Task<TResult>;

        /// <summary>
        /// Gets the result value of this task.
        /// </summary>
        public virtual TResult Result => this.AwaiterTask.Result;

        /// <summary>
        /// Initializes a new instance of the <see cref="ControlledTask{TResult}"/> class.
        /// </summary>
        [DebuggerStepThrough]
        internal ControlledTask(Task<TResult> task)
            : base(task)
        {
        }

        /// <summary>
        /// Gets an awaiter for this awaitable.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public new virtual ControlledTaskAwaiter<TResult> GetAwaiter() =>
            new ControlledTaskAwaiter<TResult>(this, this.AwaiterTask);

        /// <summary>
        /// Ends the wait for the completion of the task.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal virtual TResult GetResult(TaskAwaiter<TResult> awaiter) => awaiter.GetResult();

        /// <summary>
        /// Sets the action to perform when the task completes.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal virtual void OnCompleted(Action continuation, TaskAwaiter<TResult> awaiter) =>
            awaiter.OnCompleted(continuation);

        /// <summary>
        /// Schedules the continuation action that is invoked when the task completes.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal virtual void UnsafeOnCompleted(Action continuation, TaskAwaiter<TResult> awaiter) =>
            awaiter.UnsafeOnCompleted(continuation);

        /// <summary>
        /// Configures an awaiter used to await this task.
        /// </summary>
        /// <param name="continueOnCapturedContext">
        /// True to attempt to marshal the continuation back to the original context captured; otherwise, false.
        /// </param>
        public new virtual ConfiguredControlledTaskAwaitable<TResult> ConfigureAwait(bool continueOnCapturedContext) =>
            new ConfiguredControlledTaskAwaitable<TResult>(this, this.AwaiterTask, continueOnCapturedContext);

        /// <summary>
        /// Ends the wait for the completion of the task.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal virtual TResult GetResult(ConfiguredTaskAwaitable<TResult>.ConfiguredTaskAwaiter awaiter) =>
            awaiter.GetResult();

        /// <summary>
        /// Sets the action to perform when the task completes.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal virtual void OnCompleted(Action continuation, ConfiguredTaskAwaitable<TResult>.ConfiguredTaskAwaiter awaiter) =>
            awaiter.OnCompleted(continuation);

        /// <summary>
        /// Schedules the continuation action that is invoked when the task completes.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal virtual void UnsafeOnCompleted(Action continuation, ConfiguredTaskAwaitable<TResult>.ConfiguredTaskAwaiter awaiter) =>
            awaiter.UnsafeOnCompleted(continuation);
    }
}
