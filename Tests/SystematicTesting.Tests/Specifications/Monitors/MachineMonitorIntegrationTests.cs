﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Coyote.Actors;
using Microsoft.Coyote.Specifications;
using Xunit;
using Xunit.Abstractions;

namespace Microsoft.Coyote.SystematicTesting.Tests.Specifications
{
    public class MachineMonitorIntegrationTests : BaseSystematicTest
    {
        public MachineMonitorIntegrationTests(ITestOutputHelper output)
            : base(output)
        {
        }

        private class CheckE : Event
        {
            public bool Value;

            public CheckE(bool v)
            {
                this.Value = v;
            }
        }

        private class M1<T> : StateMachine
        {
            private readonly bool Test = false;

            [Start]
            [OnEntry(nameof(InitOnEntry))]
            private class Init : State
            {
            }

            private void InitOnEntry()
            {
                this.Monitor<T>(new CheckE(this.Test));
            }
        }

        private class M2 : StateMachine
        {
            private readonly bool Test = false;

            [Start]
            [OnEntry(nameof(InitOnEntry))]
            private class Init : State
            {
            }

            private void InitOnEntry()
            {
                this.Monitor<Spec2>(new CheckE(true));
                this.Monitor<Spec2>(new CheckE(this.Test));
            }
        }

        private class Spec1 : Monitor
        {
            [Start]
            [OnEventDoAction(typeof(CheckE), nameof(Check))]
            private class Checking : State
            {
            }

            private void Check(Event e)
            {
                this.Assert((e as CheckE).Value == true);
            }
        }

        private class Spec2 : Monitor
        {
            [Start]
            [OnEventDoAction(typeof(CheckE), nameof(Check))]
            private class Checking : State
            {
            }

            private void Check()
            {
                // this.Assert((e as CheckE).Value == true); // passes
            }
        }

        private class Spec3 : Monitor
        {
            [Start]
            [OnEventDoAction(typeof(CheckE), nameof(Check))]
            private class Checking : State
            {
            }

            private void Check(Event e)
            {
                this.Assert((e as CheckE).Value == false);
            }
        }

        [Fact(Timeout = 5000)]
        public void TestMachineMonitorIntegration1()
        {
            this.TestWithError(r =>
            {
                r.RegisterMonitor(typeof(Spec1));
                r.CreateActor(typeof(M1<Spec1>));
            },
            configuration: GetConfiguration().WithStrategy("dfs"),
            expectedError: "Detected an assertion failure.",
            replay: true);
        }

        [Fact(Timeout = 5000)]
        public void TestMachineMonitorIntegration2()
        {
            this.Test(r =>
            {
                r.RegisterMonitor(typeof(Spec2));
                r.CreateActor(typeof(M2));
            },
            configuration: GetConfiguration().WithStrategy("dfs"));
        }

        [Fact(Timeout = 5000)]
        public void TestMachineMonitorIntegration3()
        {
            this.Test(r =>
            {
                r.RegisterMonitor(typeof(Spec3));
                r.CreateActor(typeof(M1<Spec3>));
            },
            configuration: GetConfiguration().WithStrategy("dfs"));
        }
    }
}
