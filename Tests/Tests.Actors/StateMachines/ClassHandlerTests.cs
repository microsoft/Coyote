﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Coyote.Tests.Common.Actors;
using Xunit;
using Xunit.Abstractions;

namespace Microsoft.Coyote.Actors.Tests.StateMachines
{
    /// <summary>
    /// Tests that StateMachines can also fall back on class level OnEventDoActions that
    /// all Actors can define.
    /// </summary>
    public class ClassHandlerTests : BaseActorTest
    {
        public ClassHandlerTests(ITestOutputHelper output)
            : base(output)
        {
        }

        private class E1 : Event
        {
        }

        private class E2 : Event
        {
        }

        private class E3 : Event
        {
        }

        [OnEventDoAction(typeof(E1), nameof(HandleE1))]
        private class M1 : TraceableStateMachine
        {
            [Start]
            private class Init : State
            {
            }

            private void HandleE1()
            {
                this.Trace("HandleE1");
                this.OnFinalEvent();
            }
        }

        [OnEventDoAction(typeof(E1), nameof(HandleE1))]
        private class M2 : TraceableStateMachine
        {
            [Start]
            [OnEventDoAction(typeof(E1), nameof(HandleInitE1))]
            private class Init : State
            {
            }

            private void HandleE1()
            {
                this.Trace("HandleE1");
                this.OnFinalEvent();
            }

            private void HandleInitE1()
            {
                this.Trace("HandleInitE1");
                this.OnFinalEvent();
            }
        }

        [OnEventDoAction(typeof(E1), nameof(HandleE1))]
        private class M3 : TraceableStateMachine
        {
            [Start]
            [OnEntry(nameof(OnInitEntry))]
            [DeferEvents(typeof(E1))]
            private class Init : State
            {
            }

            private void OnInitEntry()
            {
                this.Trace("OnInitEntry");
                this.RaiseGotoStateEvent<Active>();
            }

            [OnEventDoAction(typeof(E1), nameof(HandleActiveE1))]
            private class Active : State
            {
            }

            private void HandleE1()
            {
                this.Trace("HandleE1");
                this.OnFinalEvent();
            }

            private void HandleActiveE1()
            {
                this.Trace("HandleActiveE1");
                this.OnFinalEvent();
            }
        }

        [OnEventDoAction(typeof(E1), nameof(HandleE1))]
        private class M4 : TraceableStateMachine
        {
            [Start]
            [OnEventDoAction(typeof(WildCardEvent), nameof(HandleWildCard))]
            private class Init : State
            {
            }

            private void HandleE1()
            {
                this.Trace("HandleE1");
                this.OnFinalEvent();
            }

            private void HandleWildCard()
            {
                this.Trace("HandleWildCard");
                this.OnFinalEvent();
            }
        }

        [Fact(Timeout = 5000)]
        public void TestClassEventHandler()
        {
            this.Test(async (IActorRuntime runtime) =>
            {
                var op = new EventGroupList();
                var id = runtime.CreateActor(typeof(M1), null, op);
                runtime.SendEvent(id, new E1());
                await this.GetResultAsync(op.Task);
                var actual = op.ToString();
                Assert.Equal("HandleE1", actual);
            });
        }

        [Fact(Timeout = 5000)]
        public void TestClassEventHandlerOverride()
        {
            this.Test(async (IActorRuntime runtime) =>
            {
                var op = new EventGroupList();
                var id = runtime.CreateActor(typeof(M2), null, op);
                runtime.SendEvent(id, new E1());
                await this.GetResultAsync(op.Task);
                var actual = op.ToString();
                Assert.Equal("HandleInitE1", actual);
            });
        }

        [Fact(Timeout = 5000)]
        public void TestClassEventHandlerDeferOverride()
        {
            this.Test(async (IActorRuntime runtime) =>
            {
                var op = new EventGroupList();
                var id = runtime.CreateActor(typeof(M3), null, op);
                runtime.SendEvent(id, new E1());
                await this.GetResultAsync(op.Task);
                var actual = op.ToString();
                Assert.Equal("OnInitEntry, CurrentState=Active, HandleActiveE1", actual);
            });
        }

        [Fact(Timeout = 5000)]
        public void TestClassEventHandlerWildcardOverride()
        {
            this.Test(async (IActorRuntime runtime) =>
            {
                var op = new EventGroupList();
                var id = runtime.CreateActor(typeof(M4), null, op);
                runtime.SendEvent(id, new E1());
                await this.GetResultAsync(op.Task);
                var actual = op.ToString();
                Assert.Equal("HandleWildCard", actual);
            });
        }
    }
}
