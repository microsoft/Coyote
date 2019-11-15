﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Coyote.Specifications;
using Microsoft.Coyote.Tests.Common.Actors;
using Xunit;
using Xunit.Abstractions;

namespace Microsoft.Coyote.TestingServices.Tests.Specifications
{
    public class OverloadedEventHandlerTest : BaseTest
    {
        public OverloadedEventHandlerTest(ITestOutputHelper output)
            : base(output)
        {
        }

        private class Safety : Monitor
        {
            [Start]
            [OnEntry(nameof(InitOnEntry))]
            [OnEventDoAction(typeof(UnitEvent), nameof(HandleUnitEvent))]
            public class Init : State
            {
            }

            private void InitOnEntry()
            {
                this.RaiseEvent(new UnitEvent());
            }

            private void HandleUnitEvent()
            {
            }

#pragma warning disable CA1801 // Parameter not used
#pragma warning disable IDE0060 // Parameter not used
            private void HandleUnitEvent(int k)
            {
            }
#pragma warning restore IDE0060 // Parameter not used
#pragma warning restore CA1801 // Parameter not used
        }

        [Fact(Timeout=5000)]
        public void TestOverloadedMonitorEventHandler()
        {
            this.Test(r =>
            {
                r.RegisterMonitor(typeof(Safety));
            });
        }
    }
}
