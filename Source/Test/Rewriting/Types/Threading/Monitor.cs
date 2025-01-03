// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using Microsoft.Coyote.Rewriting.Types.Threading.Tasks;
using Microsoft.Coyote.Runtime;
using SystemInterlocked = System.Threading.Interlocked;
using SystemSynchronizationLockException = System.Threading.SynchronizationLockException;
using SystemThreading = System.Threading;

namespace Microsoft.Coyote.Rewriting.Types.Threading
{
    /// <summary>
    /// Provides methods for monitors that can be controlled during testing.
    /// </summary>
    /// <remarks>This type is intended for compiler use rather than use directly in code.</remarks>
    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
    public static class Monitor
    {
        /// <summary>
        /// Acquires an exclusive lock on the specified object.
        /// </summary>
        public static void Enter(object obj)
        {
            var runtime = CoyoteRuntime.Current;
            if (runtime.SchedulingPolicy is SchedulingPolicy.Interleaving)
            {
                SynchronizedBlock.Lock(obj);
            }
            else
            {
                if (runtime.SchedulingPolicy is SchedulingPolicy.Fuzzing &&
                    runtime.TryGetExecutingOperation(out ControlledOperation current))
                {
                    runtime.DelayOperation(current);
                }

                SystemThreading.Monitor.Enter(obj);
            }
        }

        /// <summary>
        /// Acquires an exclusive lock on the specified object.
        /// </summary>
        public static void Enter(object obj, ref bool lockTaken)
        {
            var runtime = CoyoteRuntime.Current;
            if (runtime.SchedulingPolicy is SchedulingPolicy.Interleaving)
            {
                lockTaken = SynchronizedBlock.Lock(obj).IsLockTaken;
            }
            else
            {
                if (runtime.SchedulingPolicy is SchedulingPolicy.Fuzzing &&
                    runtime.TryGetExecutingOperation(out ControlledOperation current))
                {
                    runtime.DelayOperation(current);
                }

                SystemThreading.Monitor.Enter(obj, ref lockTaken);
            }
        }

        /// <summary>
        /// Releases an exclusive lock on the specified object.
        /// </summary>
        public static void Exit(object obj)
        {
            var runtime = CoyoteRuntime.Current;
            if (runtime.SchedulingPolicy is SchedulingPolicy.Interleaving)
            {
                var block = SynchronizedBlock.Find(obj) ??
                    throw new SystemThreading.SynchronizationLockException();
                block.Exit();
            }
            else
            {
                SystemThreading.Monitor.Exit(obj);
            }
        }

        /// <summary>
        /// Determines whether the current thread holds the lock on the specified object.
        /// </summary>
        public static bool IsEntered(object obj)
        {
            var runtime = CoyoteRuntime.Current;
            if (runtime.SchedulingPolicy is SchedulingPolicy.Interleaving)
            {
                var block = SynchronizedBlock.Find(obj) ??
                    throw new SystemThreading.SynchronizationLockException();
                return block.IsEntered();
            }

            return SystemThreading.Monitor.IsEntered(obj);
        }

        /// <summary>
        /// Notifies a thread in the waiting queue of a change in the locked object's state.
        /// </summary>
        public static void Pulse(object obj)
        {
            var runtime = CoyoteRuntime.Current;
            if (runtime.SchedulingPolicy is SchedulingPolicy.Interleaving)
            {
                var block = SynchronizedBlock.Find(obj) ??
                    throw new SystemThreading.SynchronizationLockException();
                block.Pulse();
            }
            else
            {
                SystemThreading.Monitor.Pulse(obj);
            }
        }

        /// <summary>
        /// Notifies all waiting threads of a change in the object's state.
        /// </summary>
        public static void PulseAll(object obj)
        {
            var runtime = CoyoteRuntime.Current;
            if (runtime.SchedulingPolicy is SchedulingPolicy.Interleaving)
            {
                var block = SynchronizedBlock.Find(obj) ??
                    throw new SystemThreading.SynchronizationLockException();
                block.PulseAll();
            }
            else
            {
                SystemThreading.Monitor.PulseAll(obj);
            }
        }

        /// <summary>
        /// Attempts, for the specified amount of time, to acquire an exclusive lock on the specified object,
        /// and atomically sets a value that indicates whether the lock was taken.
        /// </summary>
        public static void TryEnter(object obj, TimeSpan timeout, ref bool lockTaken)
        {
            var runtime = CoyoteRuntime.Current;
            if (runtime.SchedulingPolicy is SchedulingPolicy.Interleaving)
            {
                // TODO: how to implement this timeout?
                lockTaken = SynchronizedBlock.Lock(obj).IsLockTaken;
            }
            else
            {
                if (runtime.SchedulingPolicy is SchedulingPolicy.Fuzzing &&
                    runtime.TryGetExecutingOperation(out ControlledOperation current))
                {
                    runtime.DelayOperation(current);
                }

                SystemThreading.Monitor.TryEnter(obj, timeout, ref lockTaken);
            }
        }

        /// <summary>
        /// Attempts, for the specified amount of time, to acquire an exclusive lock on the specified object,
        /// and atomically sets a value that indicates whether the lock was taken.
        /// </summary>
        public static bool TryEnter(object obj, TimeSpan timeout)
        {
            var runtime = CoyoteRuntime.Current;
            if (runtime.SchedulingPolicy is SchedulingPolicy.Interleaving)
            {
                // TODO: how to implement this timeout?
                return SynchronizedBlock.Lock(obj).IsLockTaken;
            }
            else if (runtime.SchedulingPolicy is SchedulingPolicy.Fuzzing &&
                runtime.TryGetExecutingOperation(out ControlledOperation current))
            {
                runtime.DelayOperation(current);
            }

            return SystemThreading.Monitor.TryEnter(obj, timeout);
        }

        /// <summary>
        /// Attempts, for the specified number of milliseconds, to acquire an exclusive lock on the specified object,
        /// and atomically sets a value that indicates whether the lock was taken.
        /// </summary>
        public static void TryEnter(object obj, int millisecondsTimeout, ref bool lockTaken)
        {
            var runtime = CoyoteRuntime.Current;
            if (runtime.SchedulingPolicy is SchedulingPolicy.Interleaving)
            {
                // TODO: how to implement this timeout?
                lockTaken = SynchronizedBlock.Lock(obj).IsLockTaken;
            }
            else
            {
                if (runtime.SchedulingPolicy is SchedulingPolicy.Fuzzing &&
                    runtime.TryGetExecutingOperation(out ControlledOperation current))
                {
                    runtime.DelayOperation(current);
                }

                SystemThreading.Monitor.TryEnter(obj, millisecondsTimeout, ref lockTaken);
            }
        }

        /// <summary>
        /// Attempts to acquire an exclusive lock on the specified object, and atomically
        /// sets a value that indicates whether the lock was taken.
        /// </summary>
        public static void TryEnter(object obj, ref bool lockTaken)
        {
            var runtime = CoyoteRuntime.Current;
            if (runtime.SchedulingPolicy is SchedulingPolicy.Interleaving)
            {
                // TODO: how to implement this timeout?
                lockTaken = SynchronizedBlock.Lock(obj).IsLockTaken;
            }
            else
            {
                if (runtime.SchedulingPolicy is SchedulingPolicy.Fuzzing &&
                    runtime.TryGetExecutingOperation(out ControlledOperation current))
                {
                    runtime.DelayOperation(current);
                }

                SystemThreading.Monitor.TryEnter(obj, ref lockTaken);
            }
        }

        /// <summary>
        /// Attempts to acquire an exclusive lock on the specified object.
        /// </summary>
        public static bool TryEnter(object obj)
        {
            var runtime = CoyoteRuntime.Current;
            if (runtime.SchedulingPolicy is SchedulingPolicy.Interleaving)
            {
                return SynchronizedBlock.Lock(obj).IsLockTaken;
            }
            else if (runtime.SchedulingPolicy is SchedulingPolicy.Fuzzing &&
                runtime.TryGetExecutingOperation(out ControlledOperation current))
            {
                runtime.DelayOperation(current);
            }

            return SystemThreading.Monitor.TryEnter(obj);
        }

        /// <summary>
        /// Releases the lock on an object and blocks the current thread until it reacquires the lock.
        /// </summary>
        public static bool Wait(object obj)
        {
            var runtime = CoyoteRuntime.Current;
            if (runtime.SchedulingPolicy is SchedulingPolicy.Interleaving)
            {
                var block = SynchronizedBlock.Find(obj) ??
                    throw new SystemThreading.SynchronizationLockException();
                return block.Wait();
            }

            return SystemThreading.Monitor.Wait(obj);
        }

        /// <summary>
        /// Releases the lock on an object and blocks the current thread until it reacquires the lock.
        /// If the specified time-out interval elapses, the thread enters the ready queue.
        /// </summary>
        public static bool Wait(object obj, int millisecondsTimeout)
        {
            var runtime = CoyoteRuntime.Current;
            if (runtime.SchedulingPolicy is SchedulingPolicy.Interleaving)
            {
                var block = SynchronizedBlock.Find(obj) ??
                    throw new SystemThreading.SynchronizationLockException();
                return block.Wait(millisecondsTimeout);
            }

            return SystemThreading.Monitor.Wait(obj, millisecondsTimeout);
        }

        /// <summary>
        /// Releases the lock on an object and blocks the current thread until it reacquires the lock. If the
        /// specified time-out interval elapses, the thread enters the ready queue. This method also specifies
        /// whether the synchronization domain for the context (if in a synchronized context) is exited before
        /// the wait and reacquired afterward.
        /// </summary>
        public static bool Wait(object obj, int millisecondsTimeout, bool exitContext)
        {
            var runtime = CoyoteRuntime.Current;
            if (runtime.SchedulingPolicy is SchedulingPolicy.Interleaving)
            {
                var block = SynchronizedBlock.Find(obj) ??
                    throw new SystemThreading.SynchronizationLockException();

                // TODO: implement exitContext.
                return block.Wait(millisecondsTimeout);
            }

            return SystemThreading.Monitor.Wait(obj, millisecondsTimeout, exitContext);
        }

        /// <summary>
        /// Releases the lock on an object and blocks the current thread until it reacquires the lock.
        /// If the specified time-out interval elapses, the thread enters the ready queue.
        /// </summary>
        public static bool Wait(object obj, TimeSpan timeout)
        {
            var runtime = CoyoteRuntime.Current;
            if (runtime.SchedulingPolicy is SchedulingPolicy.Interleaving)
            {
                var block = SynchronizedBlock.Find(obj) ??
                    throw new SystemThreading.SynchronizationLockException();
                return block.Wait(timeout);
            }

            return SystemThreading.Monitor.Wait(obj, timeout);
        }

        /// <summary>
        /// Releases the lock on an object and blocks the current thread until it reacquires the lock.
        /// If the specified time-out interval elapses, the thread enters the ready queue. Optionally
        /// exits the synchronization domain for the synchronized context before the wait and reacquires
        /// the domain afterward.
        /// </summary>
        public static bool Wait(object obj, TimeSpan timeout, bool exitContext)
        {
            var runtime = CoyoteRuntime.Current;
            if (runtime.SchedulingPolicy is SchedulingPolicy.Interleaving)
            {
                var block = SynchronizedBlock.Find(obj) ??
                    throw new SystemThreading.SynchronizationLockException();

                // TODO: implement exitContext.
                return block.Wait(timeout);
            }

            return SystemThreading.Monitor.Wait(obj, timeout, exitContext);
        }

        /// <summary>
        /// Provides a mechanism that synchronizes access to objects.
        /// </summary>
        internal class SynchronizedBlock : IDisposable
        {
            /// <summary>
            /// Cache from synchronized objects to synchronized block instances.
            /// </summary>
            private static readonly ConcurrentDictionary<object, Lazy<SynchronizedBlock>> Cache =
                new ConcurrentDictionary<object, Lazy<SynchronizedBlock>>();

            /// <summary>
            /// The id of the <see cref="CoyoteRuntime"/> that created this semaphore.
            /// </summary>
            private readonly Guid RuntimeId;

            /// <summary>
            /// The resource id of this handle.
            /// </summary>
            protected readonly Guid ResourceId;

            /// <summary>
            /// The object used for synchronization.
            /// </summary>
            private readonly object SyncObject;

            /// <summary>
            /// True if the lock was taken, else false.
            /// </summary>
            internal bool IsLockTaken;

            /// <summary>
            /// The current owner of this synchronization object.
            /// </summary>
            private ControlledOperation Owner;

            /// <summary>
            /// Wait queue of asynchronous operations.
            /// </summary>
            private readonly List<ControlledOperation> WaitQueue;

            /// <summary>
            /// Ready queue of asynchronous operations.
            /// </summary>
            private readonly List<ControlledOperation> ReadyQueue;

            /// <summary>
            /// Queue of nondeterministically buffered pulse operations to be performed after releasing
            /// the lock. This allows modeling delayed pulse operations by the operation system.
            /// </summary>
            private readonly Queue<PulseOperation> PulseQueue;

            /// <summary>
            /// The number of times that the lock has been acquired per owner. The lock can only
            /// be acquired more than one times by the same owner. A count > 1 indicates that the
            /// invocation by the current owner is reentrant.
            /// </summary>
            private readonly Dictionary<ControlledOperation, int> LockCountMap;

            /// <summary>
            /// Used to reference count accesses to this synchronized block
            /// so that it can be removed from the cache.
            /// </summary>
            private int UseCount;

            /// <summary>
            /// The debug name of this semaphore.
            /// </summary>
            private readonly string DebugName;

            /// <summary>
            /// Initializes a new instance of the <see cref="SynchronizedBlock"/> class.
            /// </summary>
            private SynchronizedBlock(CoyoteRuntime runtime, object syncObject)
            {
                if (syncObject is null)
                {
                    throw new ArgumentNullException(nameof(syncObject));
                }

                this.RuntimeId = runtime.Id;
                this.ResourceId = Guid.NewGuid();
                this.SyncObject = syncObject;
                this.WaitQueue = new List<ControlledOperation>();
                this.ReadyQueue = new List<ControlledOperation>();
                this.PulseQueue = new Queue<PulseOperation>();
                this.LockCountMap = new Dictionary<ControlledOperation, int>();
                this.UseCount = 0;
                this.DebugName = $"lock({this.ResourceId})";
            }

            /// <summary>
            /// Called to clear out the cache of synchronized blocks in cases where an iteration
            /// may have been aborted and the cache is no longer valid, and may even hold old
            /// values that would otherwise not get cleaned up and could impact future iterations.
            /// </summary>
            internal static void ResetCache () => Cache.Clear();

            /// <summary>
            /// Creates a new <see cref="SynchronizedBlock"/> for synchronizing access
            /// to the specified object and enters the lock.
            /// </summary>
            internal static SynchronizedBlock Lock(object syncObject) =>
                Cache.GetOrAdd(syncObject, key => new Lazy<SynchronizedBlock>(
                    () => new SynchronizedBlock(CoyoteRuntime.Current, key))).Value.EnterLock();

            /// <summary>
            /// Finds the synchronized block associated with the specified synchronization object.
            /// </summary>
            internal static SynchronizedBlock Find(object syncObject) =>
                Cache.TryGetValue(syncObject, out Lazy<SynchronizedBlock> lazyMock) ? lazyMock.Value : null;

            /// <summary>
            /// Determines whether the current thread holds the lock on the sync object.
            /// </summary>
            internal bool IsEntered()
            {
                if (this.Owner != null)
                {
                    CoyoteRuntime runtime = this.GetRuntime();
                    var op = runtime.GetExecutingOperation();
                    return this.Owner == op;
                }

                return false;
            }

            private SynchronizedBlock EnterLock()
            {
                CoyoteRuntime runtime = this.GetRuntime();
                this.IsLockTaken = true;
                SystemInterlocked.Increment(ref this.UseCount);

                if (runtime.Configuration.IsLockAccessRaceCheckingEnabled && this.Owner is null)
                {
                    // If this operation is trying to acquire this lock while it is free, then inject a scheduling
                    // point to give another enabled operation the chance to race and acquire this lock.
                    runtime.ScheduleNextOperation(default, SchedulingPointType.Acquire);
                }

                if (this.Owner != null)
                {
                    var op = runtime.GetExecutingOperation();
                    if (this.Owner == op)
                    {
                        // The owner is re-entering the lock.
                        this.LockCountMap[op]++;
                        return this;
                    }
                    else
                    {
                        // Another op has the lock right now, so add the executing op
                        // to the ready queue and block it.
                        this.WaitQueue.Remove(op);
                        if (!this.ReadyQueue.Contains(op))
                        {
                            this.ReadyQueue.Add(op);
                        }

                        // Pause this operation and schedule the next enabled operation.
                        op.PauseWithResource(this.ResourceId);
                        runtime.ScheduleNextOperation(op, SchedulingPointType.Pause);

                        // This operation can finally take the lock.
                        this.LockCountMap.Add(op, 1);
                        return this;
                    }
                }

                // The executing op acquired the lock and can proceed.
                this.Owner = runtime.GetExecutingOperation();
                this.LockCountMap.Add(this.Owner, 1);
                return this;
            }

            /// <summary>
            /// Notifies a thread in the waiting queue of a change in the locked object's state.
            /// </summary>
            internal void Pulse() => this.Pulse(PulseOperation.Next);

            /// <summary>
            /// Notifies all waiting threads of a change in the object's state.
            /// </summary>
            internal void PulseAll() => this.Pulse(PulseOperation.All);

            /// <summary>
            /// Invokes the specified pulse operation.
            /// </summary>
            private void Pulse(PulseOperation pulseOperation)
            {
                CoyoteRuntime runtime = this.GetRuntime();
                var op = runtime.GetExecutingOperation();
                if (this.Owner != op)
                {
                    throw new SystemSynchronizationLockException();
                }

                if (runtime.Configuration.IsLockAccessRaceCheckingEnabled)
                {
                    // Pulse can be delayed by the operating system, so simulate this by scheduling the pulse
                    // operation to execute either immediately or after the current owner releases the lock.
                    this.PulseQueue.Enqueue(pulseOperation);
                    if (this.PulseQueue.Count is 1)
                    {
                        // Create a task for draining the queue. To optimize for testing performance,
                        // we create and maintain a single task to perform this role.
                        Task.Run(() =>
                        {
                            while (this.PulseQueue.Count > 0)
                            {
                                var pulseOperation = this.PulseQueue.Dequeue();
                                runtime.ScheduleNextOperation(default, SchedulingPointType.Default);
                                this.Pulse(runtime, pulseOperation);
                            }
                        });
                    }
                }
                else
                {
                    this.Pulse(runtime, pulseOperation);
                }
            }

            /// <summary>
            /// Invokes the specified pulse operation.
            /// </summary>
            private void Pulse(CoyoteRuntime runtime, PulseOperation pulseOperation)
            {
                if (pulseOperation is PulseOperation.Next)
                {
                    if (this.WaitQueue.Count > 0)
                    {
                        // System.Threading.Monitor has FIFO semantics.
                        var waitingOp = this.WaitQueue[0];
                        this.WaitQueue.RemoveAt(0);
                        this.ReadyQueue.Add(waitingOp);
                        runtime.LogWriter.LogDebug("[coyote::debug] Operation '{0}' is pulsed by thread '{1}'.",
                            waitingOp.Id, SystemThreading.Thread.CurrentThread.ManagedThreadId);
                    }
                }
                else
                {
                    foreach (var waitingOp in this.WaitQueue)
                    {
                        this.ReadyQueue.Add(waitingOp);
                        runtime.LogWriter.LogDebug("[coyote::debug] Operation '{0}' is pulsed by thread '{1}'.",
                            waitingOp.Id, SystemThreading.Thread.CurrentThread.ManagedThreadId);
                    }

                    this.WaitQueue.Clear();
                }

                if (this.Owner is null)
                {
                    this.UnlockNextReady();
                }
            }

            /// <summary>
            /// Releases the lock on an object and blocks the current thread until it reacquires
            /// the lock.
            /// </summary>
            internal bool Wait()
            {
                CoyoteRuntime runtime = this.GetRuntime();
                var op = runtime.GetExecutingOperation();
                if (this.Owner != op)
                {
                    throw new SystemSynchronizationLockException();
                }

                this.ReadyQueue.Remove(op);
                if (!this.WaitQueue.Contains(op))
                {
                    this.WaitQueue.Add(op);
                }

                this.UnlockNextReady();

                // Pause this operation and schedule the next enabled operation.
                op.PauseWithResource(this.ResourceId);
                runtime.LogWriter.LogDebug("[coyote::debug] Operation '{0}' is waiting on thread '{1}'.",
                    op.Id, SystemThreading.Thread.CurrentThread.ManagedThreadId);
                runtime.ScheduleNextOperation(op, SchedulingPointType.Pause);
                return true;
            }

            /// <summary>
            /// Releases the lock on an object and blocks the current thread until it reacquires
            /// the lock. If the specified time-out interval elapses, the thread enters the ready
            /// queue.
            /// </summary>
#pragma warning disable CA1801 // Parameter not used
            internal bool Wait(int millisecondsTimeout)
            {
                // TODO: how to implement timeout?
                // This is a bit more tricky to model, one way is to have a loop that checks
                // for controlled random boolean choice, and if it becomes true then it fails
                // the wait. This would be similar to timers in actors, so we want to use a
                // lower probability to not fail very frequently during systematic testing.
                // In the future we might want to introduce a RandomTimeout choice (similar to
                // RandomBoolean and RandomInteger), with the benefit being that the underlying
                // testing strategy will know that this is a timeout and perhaps treat it in a
                // more intelligent manner, but for now piggybacking on the other randoms should
                // work (as long as its not with a high probability).
                return this.Wait();
            }
#pragma warning restore CA1801 // Parameter not used

            /// <summary>
            /// Releases the lock on an object and blocks the current thread until it reacquires
            /// the lock. If the specified time-out interval elapses, the thread enters the ready
            /// queue.
            /// </summary>
#pragma warning disable CA1801 // Parameter not used
            internal bool Wait(TimeSpan timeout)
            {
                // TODO: how to implement timeout?
                return this.Wait();
            }
#pragma warning restore CA1801 // Parameter not used

            /// <summary>
            /// Assigns the lock to the next operation waiting in the ready queue, if there is one,
            /// following the FIFO semantics of monitor.
            /// </summary>
            private void UnlockNextReady()
            {
                // Preparing to unlock so give up ownership.
                this.Owner = null;
                if (this.ReadyQueue.Count > 0)
                {
                    // If there is a operation waiting in the ready queue, then awake it.
                    ControlledOperation op = this.ReadyQueue[0];
                    op.TryEnable(this.ResourceId);
                    this.ReadyQueue.RemoveAt(0);
                    this.Owner = op;
                }
            }

            internal void Exit()
            {
                CoyoteRuntime runtime = this.GetRuntime();
                var op = runtime.GetExecutingOperation();
                runtime.Assert(this.LockCountMap.ContainsKey(op),
                    "Cannot invoke Dispose without acquiring the lock.");

                this.LockCountMap[op]--;
                if (this.LockCountMap[op] is 0)
                {
                    // Only release the lock if the invocation is not reentrant.
                    this.LockCountMap.Remove(op);
                    this.UnlockNextReady();
                }

                int useCount = SystemInterlocked.Decrement(ref this.UseCount);
                if (useCount is 0 && Cache[this.SyncObject].Value == this)
                {
                    // It is safe to remove this instance from the cache.
                    Cache.TryRemove(this.SyncObject, out _);
                }
            }

            /// <summary>
            /// Returns the current runtime, asserting that it is the same runtime that created this resource.
            /// </summary>
            private CoyoteRuntime GetRuntime()
            {
                var runtime = CoyoteRuntime.Current;
                if (runtime.Id != this.RuntimeId)
                {
                    var trace = new StackTrace();
                    runtime.NotifyAssertionFailure($"Accessing '{this.DebugName}' that was created in a " +
                        $"previous test iteration with runtime id '{this.RuntimeId}':\n{trace}");
                }

                return runtime;
            }

            /// <summary>
            /// Releases resources used by the synchronized block.
            /// </summary>
            protected void Dispose(bool disposing)
            {
                if (disposing)
                {
                    this.Exit();
                }
            }

            /// <summary>
            /// Releases resources used by the synchronized block.
            /// </summary>
            public void Dispose()
            {
                this.Dispose(true);
                GC.SuppressFinalize(this);
            }

            /// <summary>
            /// The type of a pulse operation.
            /// </summary>
            private enum PulseOperation
            {
                /// <summary>
                /// Pulses the next waiting operation.
                /// </summary>
                Next,

                /// <summary>
                /// Pulses all waiting operations.
                /// </summary>
                All
            }
        }
    }
}
