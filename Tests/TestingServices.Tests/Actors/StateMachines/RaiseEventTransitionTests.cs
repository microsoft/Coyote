﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Coyote.Actors;
using Microsoft.Coyote.Tests.Common.Actors;
using Xunit;
using Xunit.Abstractions;

namespace Microsoft.Coyote.TestingServices.Tests.Actors
{
    public class RaiseEventTransitionTests : BaseTest
    {
        public RaiseEventTransitionTests(ITestOutputHelper output)
            : base(output)
        {
        }

        private class E : Event
        {
        }

        private class M1 : StateMachine
        {
            [Start]
            [OnEventDoAction(typeof(UnitEvent), nameof(HandleUnitEvent))]
            [OnEventDoAction(typeof(E), nameof(HandleE))]
            private class Init : State
            {
            }

            private Transition HandleUnitEvent() => this.RaiseEvent(new E());

            private void HandleE()
            {
                this.Assert(false, "Reached test assertion.");
            }
        }

        [Fact(Timeout=5000)]
        public void TestRaiseEventTransition()
        {
            this.TestWithError(r =>
            {
                var id = r.CreateActor(typeof(M1));
                r.SendEvent(id, UnitEvent.Instance);
            },
            configuration: GetConfiguration(),
            expectedError: "Reached test assertion.",
            replay: true);
        }

        private class M2 : StateMachine
        {
            [Start]
            [OnEntry(nameof(InitOnEntry))]
            [OnExit(nameof(ExitMethod))]
            private class Init : State
            {
            }

            private Transition InitOnEntry() => this.GotoState<Done>();

            private Transition ExitMethod() => this.RaiseEvent(UnitEvent.Instance);

            private class Done : State
            {
            }
        }

        [Fact(Timeout = 5000)]
        public void TestRaiseEventTransitionOnExit()
        {
            var expectedError = "'M2()' has performed a 'RaiseEvent' transition from an OnExit action.";
            this.TestWithError(r =>
            {
                r.CreateActor(typeof(M2));
            },
            expectedError: expectedError,
            replay: true);
        }
    }
}
