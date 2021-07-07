﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using SystemTasks = System.Threading.Tasks;

namespace Microsoft.Coyote.Tasks
{
    /// <summary>
    /// Extension methods for <see cref="SystemTasks.Task"/> and <see cref="SystemTasks.Task{TResult}"/> objects.
    /// </summary>
    internal static class TaskExtensions
    {
        /// <summary>
        /// Returns a dummy controlled <see cref="Task{TResult}"/> that wraps this uncontrolled
        /// <see cref="SystemTasks.Task{TResult}"/>.
        /// </summary>
        /// <remarks>
        /// The returned dummy controlled <see cref="Task{TResult}"/> does not actually take control of the
        /// uncontrolled <see cref="SystemTasks.Task{TResult}"/> during systematic testing, so this method
        /// should only be used to cross an interface boundary where a controlled <see cref="Task{TResult}"/>
        /// must be temporarily converted into an uncontrolled <see cref="SystemTasks.Task{TResult}"/> and
        /// then coverted back to a controlled <see cref="Task{TResult}"/>.
        /// </remarks>
        internal static Task<TResult> WrapInControlledTask<TResult>(this SystemTasks.Task<TResult> @this) =>
            new Task<TResult>(null, @this);
    }
}
