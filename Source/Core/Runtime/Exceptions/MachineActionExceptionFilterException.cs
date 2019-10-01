﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;

namespace Microsoft.Coyote.Runtime
{
    /// <summary>
    /// The exception that is thrown by the Coyote runtime upon a machine action failure.
    /// </summary>
    internal sealed class MachineActionExceptionFilterException : RuntimeException
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="MachineActionExceptionFilterException"/> class.
        /// </summary>
        /// <param name="message">Message</param>
        internal MachineActionExceptionFilterException(string message)
            : base(message)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="MachineActionExceptionFilterException"/> class.
        /// </summary>
        /// <param name="message">Message</param>
        /// <param name="innerException">Inner exception</param>
        internal MachineActionExceptionFilterException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}
