﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Microsoft.Coyote.Runtime;

namespace Microsoft.Coyote.Threading.Tasks
{
    /// <summary>
    /// Represents a builder for asynchronous methods that return a <see cref="ControlledTask"/>.
    /// This type is intended for compiler use only.
    /// </summary>
    /// <remarks>This type is intended for compiler use rather than use directly in code.</remarks>
    [StructLayout(LayoutKind.Auto)]
    public struct AsyncControlledTaskMethodBuilder
    {
        /// <summary>
        /// The <see cref="AsyncTaskMethodBuilder"/> to which most operations are delegated.
        /// </summary>
#pragma warning disable IDE0044 // Add readonly modifier
        private AsyncTaskMethodBuilder MethodBuilder;
#pragma warning restore IDE0044 // Add readonly modifier

        /// <summary>
        /// True, if completed synchronously and successfully, else false.
        /// </summary>
        private bool IsCompleted;

        /// <summary>
        /// True, if the builder should be used for setting/getting the result, else false.
        /// </summary>
        private bool UseBuilder;

        /// <summary>
        /// Gets the task for this builder.
        /// </summary>
        public ControlledTask Task
        {
            [DebuggerHidden]
            get
            {
                if (this.IsCompleted)
                {
                    IO.Debug.WriteLine("<AsyncBuilder> Creating completed builder task '{0}' (isCompleted {1}) from task '{2}'.",
                        this.MethodBuilder.Task.Id, this.MethodBuilder.Task.IsCompleted, System.Threading.Tasks.Task.CurrentId);
                    return ControlledTask.CompletedTask;
                }
                else
                {
                    IO.Debug.WriteLine("<AsyncBuilder> Creating builder task '{0}' (isCompleted {1}) from task '{2}'.",
                        this.MethodBuilder.Task.Id, this.MethodBuilder.Task.IsCompleted, System.Threading.Tasks.Task.CurrentId);
                    this.UseBuilder = true;
                    return CoyoteRuntime.Provider.Current.CreateAsyncControlledTaskMethodBuilderTask(this.MethodBuilder.Task);
                }
            }
        }

        /// <summary>
        /// Creates an instance of the <see cref="AsyncControlledTaskMethodBuilder"/> struct.
        /// </summary>
        [DebuggerHidden]
        public static AsyncControlledTaskMethodBuilder Create() => default;

        /// <summary>
        /// Begins running the builder with the associated state machine.
        /// </summary>
        [DebuggerStepThrough]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
#pragma warning disable CA1822 // Mark members as static
        public void Start<TStateMachine>(ref TStateMachine stateMachine)
            where TStateMachine : IAsyncStateMachine
        {
            IO.Debug.WriteLine("<AsyncBuilder> Start state machine from task '{0}'.", System.Threading.Tasks.Task.CurrentId);
            CoyoteRuntime.Provider.Current.NotifyStartedAsyncControlledTaskStateMachine(stateMachine.GetType());
            this.MethodBuilder.Start(ref stateMachine);
        }
#pragma warning restore CA1822 // Mark members as static

        /// <summary>
        /// Associates the builder with the specified state machine.
        /// </summary>
        [DebuggerHidden]
        public void SetStateMachine(IAsyncStateMachine stateMachine) =>
            this.MethodBuilder.SetStateMachine(stateMachine);

        /// <summary>
        /// Marks the task as successfully completed.
        /// </summary>
        [DebuggerHidden]
        public void SetResult()
        {
            if (this.UseBuilder)
            {
                IO.Debug.WriteLine("<AsyncBuilder> Set result of task '{0}' from task '{1}'.",
                    this.MethodBuilder.Task.Id, System.Threading.Tasks.Task.CurrentId);
                this.MethodBuilder.SetResult();
            }
            else
            {
                IO.Debug.WriteLine("<AsyncBuilder> Set result (completed) from task '{0}'.", System.Threading.Tasks.Task.CurrentId);
                this.IsCompleted = true;
            }
        }

        /// <summary>
        /// Marks the task as failed and binds the specified exception to the task.
        /// </summary>
        [DebuggerHidden]
        public void SetException(Exception exception) => this.MethodBuilder.SetException(exception);

        /// <summary>
        /// Schedules the state machine to proceed to the next action when the specified awaiter completes.
        /// </summary>
        [DebuggerHidden]
        public void AwaitOnCompleted<TAwaiter, TStateMachine>(ref TAwaiter awaiter, ref TStateMachine stateMachine)
            where TAwaiter : INotifyCompletion
            where TStateMachine : IAsyncStateMachine
        {
            this.UseBuilder = true;
            CoyoteRuntime.Provider.Current.NotifyInvokedAwaitOnCompleted(awaiter.GetType(), stateMachine.GetType());
            this.MethodBuilder.AwaitOnCompleted(ref awaiter, ref stateMachine);
        }

        /// <summary>
        /// Schedules the state machine to proceed to the next action when the specified awaiter completes.
        /// </summary>
        [DebuggerHidden]
        public void AwaitUnsafeOnCompleted<TAwaiter, TStateMachine>(ref TAwaiter awaiter, ref TStateMachine stateMachine)
            where TAwaiter : ICriticalNotifyCompletion
            where TStateMachine : IAsyncStateMachine
        {
            this.UseBuilder = true;
            CoyoteRuntime.Provider.Current.NotifyInvokedAwaitOnCompleted(awaiter.GetType(), stateMachine.GetType());
            this.MethodBuilder.AwaitUnsafeOnCompleted(ref awaiter, ref stateMachine);
        }
    }

    /// <summary>
    /// Represents a builder for asynchronous methods that return a <see cref="ControlledTask{TResult}"/>.
    /// This type is intended for compiler use only.
    /// </summary>
    /// <remarks>This type is intended for compiler use rather than use directly in code.</remarks>
    [StructLayout(LayoutKind.Auto)]
    public struct AsyncControlledTaskMethodBuilder<TResult>
    {
        /// <summary>
        /// The <see cref="AsyncTaskMethodBuilder"/> to which most operations are delegated.
        /// </summary>
#pragma warning disable IDE0044 // Add readonly modifier
        private AsyncTaskMethodBuilder<TResult> MethodBuilder;
#pragma warning restore IDE0044 // Add readonly modifier

        /// <summary>
        /// The result for this builder, if it's completed before any awaits occur.
        /// </summary>
        private TResult Result;

        /// <summary>
        /// True, if completed synchronously and successfully, else false.
        /// </summary>
        private bool IsCompleted;

        /// <summary>
        /// True, if the builder should be used for setting/getting the result, else false.
        /// </summary>
        private bool UseBuilder;

        /// <summary>
        /// Gets the task for this builder.
        /// </summary>
        public ControlledTask<TResult> Task
        {
            [DebuggerHidden]
            get
            {
                if (this.IsCompleted)
                {
                    IO.Debug.WriteLine("<AsyncBuilder> Creating completed builder task '{0}' (completed '{1}', result '{2}', result type '{3}') from task '{4}'.",
                        this.MethodBuilder.Task.Id, this.MethodBuilder.Task.IsCompleted, this.Result, typeof(TResult), System.Threading.Tasks.Task.CurrentId);
                    return ControlledTask.FromResult(this.Result);
                }
                else
                {
                    IO.Debug.WriteLine("<AsyncBuilder> Creating builder task '{0}' (completed '{1}', result '{2}', result type '{3}') from task '{4}'.",
                        this.MethodBuilder.Task.Id, this.MethodBuilder.Task.IsCompleted, this.Result, typeof(TResult), System.Threading.Tasks.Task.CurrentId);
                    this.UseBuilder = true;
                    return CoyoteRuntime.Provider.Current.CreateAsyncControlledTaskMethodBuilderTask(this.MethodBuilder.Task);
                }
            }
        }

        /// <summary>
        /// Creates an instance of the <see cref="AsyncControlledTaskMethodBuilder{TResult}"/> struct.
        /// </summary>
#pragma warning disable CA1000 // Do not declare static members on generic types
        [DebuggerHidden]
        public static AsyncControlledTaskMethodBuilder<TResult> Create() => default;
#pragma warning restore CA1000 // Do not declare static members on generic types

        /// <summary>
        /// Begins running the builder with the associated state machine.
        /// </summary>
        [DebuggerStepThrough]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
#pragma warning disable CA1822 // Mark members as static
        public void Start<TStateMachine>(ref TStateMachine stateMachine)
            where TStateMachine : IAsyncStateMachine
        {
            IO.Debug.WriteLine("<AsyncBuilder> Start state machine from task '{0}' (result type '{1}').",
                System.Threading.Tasks.Task.CurrentId, typeof(TResult));
            CoyoteRuntime.Provider.Current.NotifyStartedAsyncControlledTaskStateMachine(stateMachine.GetType());
            this.MethodBuilder.Start(ref stateMachine);
        }
#pragma warning restore CA1822 // Mark members as static

        /// <summary>
        /// Associates the builder with the specified state machine.
        /// </summary>
        [DebuggerHidden]
        public void SetStateMachine(IAsyncStateMachine stateMachine) =>
            this.MethodBuilder.SetStateMachine(stateMachine);

        /// <summary>
        /// Marks the task as successfully completed.
        /// </summary>
        /// <param name="result">The result to use to complete the task.</param>
        [DebuggerHidden]
        public void SetResult(TResult result)
        {
            if (this.UseBuilder)
            {
                IO.Debug.WriteLine("<AsyncBuilder> Set result with type '{0}' of task '{1}' from task '{2}'.",
                    typeof(TResult), this.MethodBuilder.Task.Id, System.Threading.Tasks.Task.CurrentId);
                this.MethodBuilder.SetResult(result);
            }
            else
            {
                IO.Debug.WriteLine("<AsyncBuilder> Set completed result '{0}' with type '{1}' from task '{2}'.",
                    result, typeof(TResult), System.Threading.Tasks.Task.CurrentId);
                this.Result = result;
                this.IsCompleted = true;
            }
        }

        /// <summary>
        /// Marks the task as failed and binds the specified exception to the task.
        /// </summary>
        [DebuggerHidden]
        public void SetException(Exception exception) => this.MethodBuilder.SetException(exception);

        /// <summary>
        /// Schedules the state machine to proceed to the next action when the specified awaiter completes.
        /// </summary>
        [DebuggerHidden]
        public void AwaitOnCompleted<TAwaiter, TStateMachine>(ref TAwaiter awaiter, ref TStateMachine stateMachine)
            where TAwaiter : INotifyCompletion
            where TStateMachine : IAsyncStateMachine
        {
            this.UseBuilder = true;
            CoyoteRuntime.Provider.Current.NotifyInvokedAwaitOnCompleted(awaiter.GetType(), stateMachine.GetType());
            this.MethodBuilder.AwaitOnCompleted(ref awaiter, ref stateMachine);
        }

        /// <summary>
        /// Schedules the state machine to proceed to the next action when the specified awaiter completes.
        /// </summary>
        [DebuggerHidden]
        public void AwaitUnsafeOnCompleted<TAwaiter, TStateMachine>(ref TAwaiter awaiter, ref TStateMachine stateMachine)
            where TAwaiter : ICriticalNotifyCompletion
            where TStateMachine : IAsyncStateMachine
        {
            this.UseBuilder = true;
            CoyoteRuntime.Provider.Current.NotifyInvokedAwaitOnCompleted(awaiter.GetType(), stateMachine.GetType());
            this.MethodBuilder.AwaitUnsafeOnCompleted(ref awaiter, ref stateMachine);
        }
    }
}
