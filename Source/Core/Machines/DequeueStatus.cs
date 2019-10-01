﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

namespace Microsoft.Coyote.Machines
{
    /// <summary>
    /// The status returned as the result of a dequeue operation.
    /// </summary>
    internal enum DequeueStatus
    {
        /// <summary>
        /// An event was successfully dequeued.
        /// </summary>
        Success = 0,

        /// <summary>
        /// The raised event was dequeued.
        /// </summary>
        Raised,

        /// <summary>
        /// The default event was dequeued.
        /// </summary>
        Default,

        /// <summary>
        /// No event available to dequeue.
        /// </summary>
        NotAvailable
    }
}
