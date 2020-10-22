﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System.Threading.Tasks;
using Microsoft.Coyote.Actors.SharedObjects;
using Xunit;
using Xunit.Abstractions;

namespace Microsoft.Coyote.Actors.Tests.SharedObjects
{
    public class MixedSharedObjectsTests : BaseActorTest
    {
        public MixedSharedObjectsTests(ITestOutputHelper output)
            : base(output)
        {
        }

        private class E : Event
        {
            public SharedDictionary<int, string> Dictionary;
            public SharedCounter Counter;
            public TaskCompletionSource<bool> Tcs;

            public E(SharedDictionary<int, string> dictionary, SharedCounter counter, TaskCompletionSource<bool> tcs)
            {
                this.Dictionary = dictionary;
                this.Counter = counter;
                this.Tcs = tcs;
            }
        }

        private class M : StateMachine
        {
            [Start]
            [OnEntry(nameof(InitOnEntry))]
            private class Init : State
            {
            }

            private void InitOnEntry(Event e)
            {
                var dictionary = (e as E).Dictionary;
                var counter = (e as E).Counter;
                var tcs = (e as E).Tcs;

                for (int i = 0; i < 100; i++)
                {
                    dictionary.TryAdd(i, i.ToString());
                }

                for (int i = 0; i < 100; i++)
                {
                    var b = dictionary.TryRemove(i, out string v);
                    this.Assert(b is false || v == i.ToString());

                    if (b)
                    {
                        counter.Increment();
                    }
                }

                var c = dictionary.Count;
                this.Assert(c is 0);
                tcs.TrySetResult(true);
            }
        }

        private class N : StateMachine
        {
            [Start]
            [OnEntry(nameof(InitOnEntry))]
            private class Init : State
            {
            }

            private void InitOnEntry(Event e)
            {
                var dictionary = (e as E).Dictionary;
                var counter = (e as E).Counter;
                var tcs = (e as E).Tcs;

                for (int i = 0; i < 100; i++)
                {
                    var b = dictionary.TryRemove(i, out string v);
                    this.Assert(b is false || v == i.ToString());

                    if (b)
                    {
                        counter.Increment();
                    }
                }

                tcs.TrySetResult(true);
            }
        }

        [Fact(Timeout = 5000)]
        public void TestProductionSharedObjects()
        {
            var runtime = RuntimeFactory.Create();
            var dictionary = SharedDictionary.Create<int, string>(runtime);
            var counter = SharedCounter.Create(runtime);
            var tcs1 = new TaskCompletionSource<bool>();
            var tcs2 = new TaskCompletionSource<bool>();
            var failed = false;

            runtime.OnFailure += (ex) =>
            {
                failed = true;
                tcs1.TrySetResult(true);
                tcs2.TrySetResult(true);
            };

            var m1 = runtime.CreateActor(typeof(M), new E(dictionary, counter, tcs1));
            var m2 = runtime.CreateActor(typeof(N), new E(dictionary, counter, tcs2));

            Task.WaitAll(tcs1.Task, tcs2.Task);
            Assert.False(failed);
            Assert.True(counter.GetValue() is 100);
        }
    }
}
