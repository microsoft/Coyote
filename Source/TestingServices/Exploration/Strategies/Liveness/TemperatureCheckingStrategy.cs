﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Collections.Generic;

using Monitor = Microsoft.Coyote.Specifications.Monitor;

namespace Microsoft.Coyote.TestingServices.Scheduling.Strategies
{
    /// <summary>
    /// Strategy for detecting liveness property violations using the "temperature"
    /// method. It contains a nested <see cref="ISchedulingStrategy"/> that is used
    /// for scheduling decisions. Note that liveness property violations are checked
    /// only if the nested strategy is fair.
    /// </summary>
    internal sealed class TemperatureCheckingStrategy : LivenessCheckingStrategy
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="TemperatureCheckingStrategy"/> class.
        /// </summary>
        internal TemperatureCheckingStrategy(Configuration configuration, List<Monitor> monitors, ISchedulingStrategy strategy)
            : base(configuration, monitors, strategy)
        {
        }

        /// <inheritdoc/>
        public override bool GetNext(out IAsyncOperation next, IEnumerable<IAsyncOperation> ops, IAsyncOperation current)
        {
            this.CheckLivenessTemperature();
            return this.SchedulingStrategy.GetNext(out next, ops, current);
        }

        /// <inheritdoc/>
        public override bool GetNextBooleanChoice(int maxValue, out bool next)
        {
            this.CheckLivenessTemperature();
            return this.SchedulingStrategy.GetNextBooleanChoice(maxValue, out next);
        }

        /// <inheritdoc/>
        public override bool GetNextIntegerChoice(int maxValue, out int next)
        {
            this.CheckLivenessTemperature();
            return this.SchedulingStrategy.GetNextIntegerChoice(maxValue, out next);
        }

        /// <summary>
        /// Checks the liveness temperature of each monitor, and
        /// reports an error if one of the liveness monitors has
        /// passed the temperature threshold.
        /// </summary>
        private void CheckLivenessTemperature()
        {
            if (this.IsFair())
            {
                foreach (var monitor in this.Monitors)
                {
                    monitor.CheckLivenessTemperature();
                }
            }
        }
    }
}
