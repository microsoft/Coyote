﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Coyote.Actors;
using Xunit;
using Xunit.Abstractions;

namespace Microsoft.Coyote.TestingServices.Tests
{
    public class MethodCallTest : BaseTest
    {
        public MethodCallTest(ITestOutputHelper output)
            : base(output)
        {
        }

        private class E : Event
        {
        }

        private class M : StateMachine
        {
            private int X;

            [Start]
            [OnEntry(nameof(InitOnEntry))]
            private class Init : MachineState
            {
            }

            private void InitOnEntry()
            {
                this.X = 2;
                Foo(1, 3, this.X);
            }

#pragma warning disable CA1801 // Parameter not used
            private static int Foo(int x, int y, int z) => 0;
#pragma warning restore CA1801 // Parameter not used
        }

        [Fact(Timeout=5000)]
        public void TestMethodCall()
        {
            this.Test(r =>
            {
                r.CreateMachine(typeof(M));
            });
        }
    }
}
