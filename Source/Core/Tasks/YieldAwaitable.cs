﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Runtime.CompilerServices;
using Microsoft.Coyote.SystematicTesting;
using SystemCompiler = System.Runtime.CompilerServices;

namespace Microsoft.Coyote.Tasks
{
    /// <summary>
    /// Implements an awaitable that asynchronously yields back to the current context when awaited.
    /// This type is intended for compiler use only.
    /// </summary>
    /// <remarks>This type is intended for compiler use rather than use directly in code.</remarks>
    [System.ComponentModel.EditorBrowsable(System.ComponentModel.EditorBrowsableState.Never)]
    public readonly struct YieldAwaitable
    {
        /// <summary>
        /// Responsible for controlling the execution of tasks during systematic testing.
        /// </summary>
        private readonly ControlledRuntime Runtime;

        /// <summary>
        /// Gets an awaiter for this awaitable.
        /// </summary>
        public YieldAwaiter GetAwaiter() => new YieldAwaiter(this.Runtime, default);

        /// <summary>
        /// Initializes a new instance of the <see cref="YieldAwaitable"/> struct.
        /// </summary>
        internal YieldAwaitable(ControlledRuntime runtime)
        {
            this.Runtime = runtime;
        }

        /// <summary>
        /// Provides an awaiter that switches into a target environment.
        /// This type is intended for compiler use only.
        /// </summary>
        /// <remarks>This type is intended for compiler use rather than use directly in code.</remarks>
        public readonly struct YieldAwaiter : ICriticalNotifyCompletion, INotifyCompletion
        {
            /// <summary>
            /// Responsible for controlling the execution of tasks during systematic testing.
            /// </summary>
            private readonly ControlledRuntime Runtime;

            /// <summary>
            /// The internal yield awaiter.
            /// </summary>
            private readonly SystemCompiler.YieldAwaitable.YieldAwaiter Awaiter;

            /// <summary>
            /// Gets a value that indicates whether a yield is not required.
            /// </summary>
#pragma warning disable CA1822 // Mark members as static
            public bool IsCompleted => false;
#pragma warning restore CA1822 // Mark members as static

            /// <summary>
            /// Initializes a new instance of the <see cref="YieldAwaiter"/> struct.
            /// </summary>
            internal YieldAwaiter(ControlledRuntime runtime, SystemCompiler.YieldAwaitable.YieldAwaiter awaiter)
            {
                this.Runtime = runtime;
                this.Awaiter = awaiter;
            }

            /// <summary>
            /// Ends the await operation.
            /// </summary>
            public void GetResult()
            {
                this.Runtime?.OnYieldAwaiterGetResult();
                this.Awaiter.GetResult();
            }

            /// <summary>
            /// Posts the continuation action back to the current context.
            /// </summary>
            public void OnCompleted(Action continuation)
            {
                if (this.Runtime is null)
                {
                    this.Awaiter.OnCompleted(continuation);
                }
                else
                {
                    this.Runtime.ScheduleYieldAwaiterContinuation(continuation);
                }
            }

            /// <summary>
            /// Posts the continuation action back to the current context.
            /// </summary>
            public void UnsafeOnCompleted(Action continuation)
            {
                if (this.Runtime is null)
                {
                    this.Awaiter.UnsafeOnCompleted(continuation);
                }
                else
                {
                    this.Runtime.ScheduleYieldAwaiterContinuation(continuation);
                }
            }
        }
    }
}
