﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Threading.Tasks;
using Microsoft.Coyote.Specifications;
using Xunit;
using Xunit.Abstractions;

namespace Microsoft.Coyote.BugFinding.Tests
{
    public class AsyncInvocationTests : BaseBugFindingTest
    {
        public AsyncInvocationTests(ITestOutputHelper output)
            : base(output)
        {
        }

        private static async Task<T> InvokeAsync<T>(T value)
        {
            await Task.CompletedTask;
            return value;
        }

        [Fact(Timeout = 5000)]
        public void TestAsyncInvocation()
        {
            this.TestWithError(async () =>
            {
                int result = await InvokeAsync(3);
                Specification.Assert(result is 3, "Unexpected value {0}.", result);
                Specification.Assert(false, "Reached test assertion.");
            },
            configuration: this.GetConfiguration().WithTestingIterations(200),
            expectedError: "Reached test assertion.",
            replay: true);
        }

        [Fact(Timeout = 5000)]
        public void TestCompletedTask()
        {
            Task task = Task.CompletedTask;
            Assert.True(task.IsCompleted);
        }
    }
}
