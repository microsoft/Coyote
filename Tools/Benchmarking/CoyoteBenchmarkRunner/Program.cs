﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using BenchmarkDotNet.Running;
using Microsoft.Coyote.Benchmarking.Creation;
using Microsoft.Coyote.Benchmarking.Messaging;

namespace Microsoft.Coyote.Benchmarking
{
    /// <summary>
    /// The Coyote performance benchmark runner.
    /// </summary>
    internal class Program
    {
#pragma warning disable CA1801 // Parameter not used
        private static void Main(string[] args)
        {
            // BenchmarkSwitcher.FromAssembly(typeof(Program).GetTypeInfo().Assembly).Run(args);
            BenchmarkRunner.Run<MachineCreationThroughputBenchmark>();
            BenchmarkRunner.Run<ExchangeEventLatencyBenchmark>();
            BenchmarkRunner.Run<SendEventThroughputBenchmark>();
            BenchmarkRunner.Run<DequeueEventThroughputBenchmark>();
        }
#pragma warning restore CA1801 // Parameter not used
    }
}
