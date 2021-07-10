﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Threading.Tasks;
using Xunit;
using Xunit.Abstractions;

namespace Microsoft.Coyote.Actors.Tests.StateMachines
{
    public class OnHaltTests : BaseActorTest
    {
        public OnHaltTests(ITestOutputHelper output)
            : base(output)
        {
        }

        private class E : Event
        {
            public ActorId Id;
            public TaskCompletionSource<bool> Tcs;

            public E()
            {
            }

            public E(ActorId id)
            {
                this.Id = id;
            }

            public E(TaskCompletionSource<bool> tcs)
            {
                this.Tcs = tcs;
            }
        }

        private class M1 : StateMachine
        {
            [Start]
            [OnEntry(nameof(InitOnEntry))]
            private class Init : State
            {
            }

            private void InitOnEntry() => this.RaiseHaltEvent();

            protected override Task OnHaltAsync(Event e)
            {
                this.Assert(false);
                return Task.CompletedTask;
            }
        }

        [Fact(Timeout = 5000)]
        public async Task TestHaltCalled()
        {
            await this.RunAsync(async r =>
            {
                var failed = false;
                var tcs = new TaskCompletionSource<bool>();
                r.OnFailure += (ex) =>
                {
                    failed = true;
                    tcs.SetResult(true);
                };

                r.CreateActor(typeof(M1));

                await this.WaitAsync(tcs.Task);
                Assert.True(failed);
            },
            handleFailures: false);
        }

        private class M2 : StateMachine
        {
            [Start]
            [OnEntry(nameof(InitOnEntry))]
            private class Init : State
            {
            }

            private void InitOnEntry() => this.RaiseHaltEvent();

            protected override async Task OnHaltAsync(Event e)
            {
                await this.ReceiveEventAsync(typeof(Event));
            }
        }

        [Fact(Timeout = 5000)]
        public async Task TestReceiveOnHalt()
        {
            await this.RunAsync(async r =>
            {
                var failed = false;
                var tcs = new TaskCompletionSource<bool>();
                r.OnFailure += (ex) =>
                {
                    failed = true;
                    tcs.SetResult(true);
                };

                r.CreateActor(typeof(M2));

                await this.WaitAsync(tcs.Task);
                Assert.True(failed);
            },
            handleFailures: false);
        }

        private class Dummy : StateMachine
        {
            [Start]
            [OnEntry(nameof(InitOnEntry))]
            private class Init : State
            {
            }

            private void InitOnEntry() => this.RaiseHaltEvent();
        }

        private class M3 : StateMachine
        {
            private TaskCompletionSource<bool> tcs;

            [Start]
            [OnEntry(nameof(InitOnEntry))]
            private class Init : State
            {
            }

            private void InitOnEntry(Event e)
            {
                this.tcs = (e as E).Tcs;
                this.RaiseHaltEvent();
            }

            protected override Task OnHaltAsync(Event e)
            {
                // No-ops, but no failure.
                this.SendEvent(this.Id, new E());
                this.RandomBoolean();
                this.Assert(true);
                this.CreateActor(typeof(Dummy));
                this.tcs.TrySetResult(true);
                return Task.CompletedTask;
            }
        }

        [Fact(Timeout = 5000)]
        public async Task TestAPIsOnHalt()
        {
            await this.RunAsync(async r =>
            {
                var failed = false;
                var tcs = new TaskCompletionSource<bool>();
                r.OnFailure += (ex) =>
                {
                    failed = true;
                    tcs.TrySetResult(true);
                };

                r.CreateActor(typeof(M3), new E(tcs));

                await this.WaitAsync(tcs.Task);
                Assert.False(failed);
            },
            handleFailures: false);
        }
    }
}
