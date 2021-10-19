﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Microsoft.Coyote.Actors.SharedObjects;
using Xunit;
using Xunit.Abstractions;

namespace Microsoft.Coyote.Actors.BugFinding.Tests.SharedObjects
{
    public class SharedRegisterTests : BaseActorBugFindingTest
    {
        public SharedRegisterTests(ITestOutputHelper output)
            : base(output)
        {
        }

        private struct S
        {
            public int Value1;
            public int Value2;

            public S(int value1, int value2)
            {
                this.Value1 = value1;
                this.Value2 = value2;
            }
        }

        private class E<T> : Event
            where T : struct
        {
            public SharedRegister<T> Counter;

            public E(SharedRegister<T> counter)
            {
                this.Counter = counter;
            }
        }

        private class Setup : Event
        {
            public bool Flag;

            public Setup(bool flag)
            {
                this.Flag = flag;
            }
        }

        private class M1 : StateMachine
        {
            [Start]
            [OnEntry(nameof(InitOnEntry))]
            private class Init : State
            {
            }

            private void InitOnEntry(Event e)
            {
                var flag = (e as Setup).Flag;

                var counter = SharedRegister.Create(this.Id.Runtime, 0);
                counter.SetValue(5);

                this.CreateActor(typeof(N1), new E<int>(counter));

                counter.Update(x =>
                {
                    if (x == 5)
                    {
                        return 6;
                    }

                    return x;
                });

                var v = counter.GetValue();

                if (flag)
                {
                    // Succeeds.
                    this.Assert(v is 2 || v == 6);
                }
                else
                {
                    // Fails.
                    this.Assert(v == 6, "Reached test assertion.");
                }
            }
        }

        private class N1 : StateMachine
        {
            [Start]
            [OnEntry(nameof(InitOnEntry))]
            private class Init : State
            {
            }

#pragma warning disable CA1822 // Mark members as static
            private void InitOnEntry(Event e)
#pragma warning restore CA1822 // Mark members as static
            {
                var counter = (e as E<int>).Counter;
                counter.SetValue(2);
            }
        }

        [Fact(Timeout = 5000)]
        public void TestSharedRegister1()
        {
            this.Test(r =>
            {
                r.CreateActor(typeof(M1), new Setup(true));
            },
            configuration: this.GetConfiguration().WithTestingIterations(100));
        }

        [Fact(Timeout = 5000)]
        public void TestSharedRegister2()
        {
            this.TestWithError(r =>
            {
                r.CreateActor(typeof(M1), new Setup(false));
            },
            configuration: this.GetConfiguration().WithTestingIterations(100),
            expectedError: "Reached test assertion.",
            replay: true);
        }

        private class M2 : StateMachine
        {
            [Start]
            [OnEntry(nameof(InitOnEntry))]
            private class Init : State
            {
            }

            private void InitOnEntry(Event e)
            {
                var flag = (e as Setup).Flag;

                var counter = SharedRegister.Create<S>(this.Id.Runtime);
                counter.SetValue(new S(1, 1));

                this.CreateActor(typeof(N2), new E<S>(counter));

                counter.Update(x =>
                {
                    return new S(x.Value1 + 1, x.Value2 + 1);
                });

                var v = counter.GetValue();

                // Succeeds.
                this.Assert(v.Value1 == v.Value2);

                if (flag)
                {
                    // Succeeds.
                    this.Assert(v.Value1 is 2 || v.Value1 == 5 || v.Value1 == 6);
                }
                else
                {
                    // Fails.
                    this.Assert(v.Value1 is 2 || v.Value1 == 6, "Reached test assertion.");
                }
            }
        }

        private class N2 : StateMachine
        {
            [Start]
            [OnEntry(nameof(InitOnEntry))]
            private class Init : State
            {
            }

#pragma warning disable CA1822 // Mark members as static
            private void InitOnEntry(Event e)
#pragma warning restore CA1822 // Mark members as static
            {
                var counter = (e as E<S>).Counter;
                counter.SetValue(new S(5, 5));
            }
        }

        [Fact(Timeout = 5000)]
        public void TestSharedRegister3()
        {
            this.Test(r =>
            {
                r.CreateActor(typeof(M2), new Setup(true));
            },
            configuration: this.GetConfiguration().WithTestingIterations(100));
        }

        [Fact(Timeout = 5000)]
        public void TestSharedRegister4()
        {
            this.TestWithError(r =>
            {
                r.CreateActor(typeof(M2), new Setup(false));
            },
            configuration: this.GetConfiguration().WithTestingIterations(100),
            expectedError: "Reached test assertion.",
            replay: true);
        }
    }
}
