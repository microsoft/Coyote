﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Coyote.Machines;
using Microsoft.Coyote.Specifications;
using Xunit;
using Xunit.Abstractions;

namespace Microsoft.Coyote.TestingServices.Tests
{
    public class GotoStateTest : BaseTest
    {
        public GotoStateTest(ITestOutputHelper output)
            : base(output)
        {
        }

        private class M : StateMachine
        {
            [Start]
            [OnEntry(nameof(InitOnEntry))]
            private class Init : MachineState
            {
            }

            private void InitOnEntry()
            {
                this.Goto<Done>();
            }

            private class Done : MachineState
            {
            }
        }

        internal static int MonitorValue;

        private class Safety : Monitor
        {
            [Start]
            [OnEntry(nameof(Init))]
            private class S1 : MonitorState
            {
            }

            [OnEntry(nameof(IncrementValue))]
            private class S2 : MonitorState
            {
            }

            private void Init()
            {
                this.Goto<S2>();
            }

            private void IncrementValue()
            {
                MonitorValue = 101;
            }
        }

        [Fact(Timeout=5000)]
        public void TestGotoMachineState()
        {
            this.Test(r =>
            {
                r.CreateMachine(typeof(M));
            });
        }

        [Fact(Timeout=5000)]
        public void TestGotoMonitorState()
        {
            this.Test(r =>
            {
                r.RegisterMonitor(typeof(Safety));
            });

            Assert.Equal(101, MonitorValue);
        }
    }
}
