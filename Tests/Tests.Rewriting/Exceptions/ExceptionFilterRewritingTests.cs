﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Coyote.Runtime;
using Microsoft.Coyote.Specifications;
using Mono.Reflection;
using Xunit;
using Xunit.Abstractions;

namespace Microsoft.Coyote.Rewriting.Tests.Exceptions
{
    public class ExceptionFilterRewritingTests : BaseRewritingTest
    {
        public ExceptionFilterRewritingTests(ITestOutputHelper output)
            : base(output)
        {
        }

        [Fact(Timeout = 5000)]
        public void TestThreadInterruptedExceptionRethrow()
        {
            this.Test(() =>
            {
                try
                {
                    CheckCatchBlockRewriting(MethodBase.GetCurrentMethod(), 0);
                }
                catch (Exception)
                {
                    throw;
                }
            },
            configuration: this.GetConfiguration().WithTestingIterations(1));
        }

        [Fact(Timeout = 5000)]
        public void TestThreadInterruptedExceptionExplicitRethrow()
        {
            this.Test(() =>
            {
                try
                {
                    CheckCatchBlockRewriting(MethodBase.GetCurrentMethod(), 0);
                }
                catch (Exception ex)
                {
#pragma warning disable CA2200 // Rethrow to preserve stack details.
                    throw ex;
#pragma warning restore CA2200 // Rethrow to preserve stack details.
                }
            },
            configuration: this.GetConfiguration().WithTestingIterations(1));
        }

        [Fact(Timeout = 5000)]
        public void TestThreadInterruptedExceptionDoubleRethrow()
        {
            this.Test(() =>
            {
                try
                {
                    try
                    {
                        CheckCatchBlockRewriting(MethodBase.GetCurrentMethod(), 0);
                    }
                    catch (Exception)
                    {
                        throw;
                    }
                }
                catch (Exception)
                {
                    throw;
                }
            },
            configuration: this.GetConfiguration().WithTestingIterations(1));
        }

        [Fact(Timeout = 5000)]
        public void TestThreadInterruptedExceptionInEmptyCatchBlock()
        {
            this.Test(() =>
            {
                try
                {
                    CheckCatchBlockRewriting(MethodBase.GetCurrentMethod(), 1);
                }
                catch (Exception)
                {
                    // Needs rewriting to not consume.
                }
            },
            configuration: this.GetConfiguration().WithTestingIterations(1));
        }

        [Fact(Timeout = 5000)]
        public void TestThreadInterruptedExceptionInDoubleEmptyCatchBlock()
        {
            this.Test(() =>
            {
                try
                {
                    try
                    {
                        CheckCatchBlockRewriting(MethodBase.GetCurrentMethod(), 2);
                    }
                    catch (Exception)
                    {
                        // Needs rewriting to not consume.
                    }
                }
                catch (Exception)
                {
                    // Needs rewriting to not consume.
                }
            },
            configuration: this.GetConfiguration().WithTestingIterations(1));
        }

        [Fact(Timeout = 5000)]
        public void TestThreadInterruptedExceptionRethrowInEmptyCatchBlock()
        {
            this.Test(() =>
            {
                try
                {
                    try
                    {
                        CheckCatchBlockRewriting(MethodBase.GetCurrentMethod(), 1);
                    }
                    catch (Exception)
                    {
                        throw;
                    }
                }
                catch (Exception)
                {
                    // Needs rewriting to not consume.
                }
            },
            configuration: this.GetConfiguration().WithTestingIterations(1));
        }

        [Fact(Timeout = 5000)]
        public void TestThreadInterruptedExceptionInNonEmptyCatchBlock()
        {
            this.Test(() =>
            {
                CheckCatchBlockRewriting(MethodBase.GetCurrentMethod(), 1);

                try
                {
                    while (true)
                    {
                        Task.Delay(1).Wait();
                    }
                }
                catch (Exception ex)
                {
                    Specification.Assert(!(ex is ThreadInterruptedException), $"Must not catch '{typeof(ThreadInterruptedException)}'.");
                }
            },
            configuration: this.GetConfiguration().WithTestingIterations(1).WithMaxSchedulingSteps(10));
        }

        [Fact(Timeout = 5000)]
        public void TestThreadInterruptedExceptionInNonEmptyCatchBlockAsync()
        {
            this.Test(async () =>
            {
                CheckCatchBlockRewriting(MethodBase.GetCurrentMethod(), 1);

                try
                {
                    while (true)
                    {
                        await Task.Delay(1);
                    }
                }
                catch (Exception ex)
                {
                    Specification.Assert(!(ex is ThreadInterruptedException), $"Must not catch '{typeof(ThreadInterruptedException)}'.");
                }
            },
            configuration: this.GetConfiguration().WithTestingIterations(1).WithMaxSchedulingSteps(10));
        }

        private static async Task<int> DelayAsync()
        {
            while (true)
            {
                await Task.Delay(1);
            }
        }

        [Fact(Timeout = 5000)]
        public void TestThreadInterruptedExceptionInNonEmptyCatchBlockGenericAsync()
        {
            this.Test(async () =>
            {
                CheckCatchBlockRewriting(MethodBase.GetCurrentMethod(), 1);

                try
                {
                    while (true)
                    {
                        await DelayAsync();
                    }
                }
                catch (Exception ex)
                {
                    Specification.Assert(!(ex is ThreadInterruptedException), $"Must not catch '{typeof(ThreadInterruptedException)}'.");
                }
            },
            configuration: this.GetConfiguration().WithTestingIterations(1).WithMaxSchedulingSteps(10));
        }

        private static void CheckCatchBlockRewriting(MethodBase methodInfo, int expectedCount)
        {
            var instructions = methodInfo.GetInstructions();
            int count = instructions.Count(i => i.OpCode == OpCodes.Call &&
                i.Operand.ToString().Contains(nameof(ExceptionProvider.ThrowIfThreadInterruptedException)));
            Specification.Assert(count == expectedCount, $"Rewrote {count} catch blocks (expected {expectedCount}).");
        }
    }
}
