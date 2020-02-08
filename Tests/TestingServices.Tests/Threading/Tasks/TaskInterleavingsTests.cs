﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Coyote.Specifications;
using Microsoft.Coyote.Tasks;
using Microsoft.Coyote.Tests.Common.Threading;
using Xunit;
using Xunit.Abstractions;

namespace Microsoft.Coyote.TestingServices.Tests.Threading.Tasks
{
    public class TaskInterleavingsTests : BaseTest
    {
        public TaskInterleavingsTests(ITestOutputHelper output)
            : base(output)
        {
        }

        private static async ControlledTask WriteAsync(SharedEntry entry, int value)
        {
            await ControlledTask.CompletedTask;
            entry.Value = value;
        }

        private static async ControlledTask WriteWithDelayAsync(SharedEntry entry, int value)
        {
            await ControlledTask.Delay(1);
            entry.Value = value;
        }

        [Fact(Timeout = 5000)]
        public void TestInterleavingsWithOneSynchronousTask()
        {
            this.Test(async () =>
            {
                SharedEntry entry = new SharedEntry();

                ControlledTask task = WriteAsync(entry, 3);
                entry.Value = 5;
                await task;

                Specification.Assert(entry.Value == 5, "Value is {0} instead of 5.", entry.Value);
            },
            configuration: GetConfiguration().WithNumberOfIterations(200));
        }

        [Fact(Timeout = 5000)]
        public void TestInterleavingsWithOneAsynchronousTask()
        {
            this.TestWithError(async () =>
            {
                SharedEntry entry = new SharedEntry();

                ControlledTask task = WriteWithDelayAsync(entry, 3);
                entry.Value = 5;
                await task;

                Specification.Assert(entry.Value == 5, "Value is {0} instead of 5.", entry.Value);
            },
            configuration: GetConfiguration().WithNumberOfIterations(200),
            expectedError: "Value is 3 instead of 5.",
            replay: true);
        }

        [Fact(Timeout = 5000)]
        public void TestInterleavingsWithOneParallelTask()
        {
            this.TestWithError(async () =>
            {
                SharedEntry entry = new SharedEntry();

                ControlledTask task = ControlledTask.Run(async () =>
                {
                    await WriteAsync(entry, 3);
                });

                await WriteAsync(entry, 5);
                await task;

                Specification.Assert(entry.Value == 5, "Value is {0} instead of 5.", entry.Value);
            },
            configuration: GetConfiguration().WithNumberOfIterations(200),
            expectedError: "Value is 3 instead of 5.",
            replay: true);
        }

        [Fact(Timeout = 5000)]
        public void TestInterleavingsWithTwoSynchronousTasks()
        {
            this.Test(async () =>
            {
                SharedEntry entry = new SharedEntry();

                ControlledTask task1 = WriteAsync(entry, 3);
                ControlledTask task2 = WriteAsync(entry, 5);

                await task1;
                await task2;

                Specification.Assert(entry.Value == 5, "Value is {0} instead of 5.", entry.Value);
            },
            configuration: GetConfiguration().WithNumberOfIterations(200));
        }

        [Fact(Timeout = 5000)]
        public void TestInterleavingsWithTwoAsynchronousTasks()
        {
            this.TestWithError(async () =>
            {
                SharedEntry entry = new SharedEntry();

                ControlledTask task1 = WriteWithDelayAsync(entry, 3);
                ControlledTask task2 = WriteWithDelayAsync(entry, 5);

                await task1;
                await task2;

                Specification.Assert(entry.Value == 5, "Value is {0} instead of 5.", entry.Value);
            },
            configuration: GetConfiguration().WithNumberOfIterations(200),
            expectedError: "Value is 3 instead of 5.",
            replay: true);
        }

        [Fact(Timeout = 5000)]
        public void TestInterleavingsWithTwoParallelTasks()
        {
            this.TestWithError(async () =>
            {
                SharedEntry entry = new SharedEntry();

                ControlledTask task1 = ControlledTask.Run(async () =>
                {
                    await WriteAsync(entry, 3);
                });

                ControlledTask task2 = ControlledTask.Run(async () =>
                {
                    await WriteAsync(entry, 5);
                });

                await task1;
                await task2;

                Specification.Assert(entry.Value == 5, "Value is {0} instead of 5.", entry.Value);
            },
            configuration: GetConfiguration().WithNumberOfIterations(200),
            expectedError: "Value is 3 instead of 5.",
            replay: true);
        }

        [Fact(Timeout = 5000)]
        public void TestInterleavingsWithNestedParallelTasks()
        {
            this.TestWithError(async () =>
            {
                SharedEntry entry = new SharedEntry();

                ControlledTask task1 = ControlledTask.Run(async () =>
                {
                    ControlledTask task2 = ControlledTask.Run(async () =>
                    {
                        await WriteAsync(entry, 5);
                    });

                    await WriteAsync(entry, 3);
                    await task2;
                });

                await task1;

                Specification.Assert(entry.Value == 5, "Value is {0} instead of 5.", entry.Value);
            },
            configuration: GetConfiguration().WithNumberOfIterations(200),
            expectedError: "Value is 3 instead of 5.",
            replay: true);
        }
    }
}
