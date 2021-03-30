﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Xunit;
using Xunit.Abstractions;

namespace Microsoft.Coyote.Actors.BugFinding.Tests.Runtime
{
    public class RandomChoiceTests : BaseActorBugFindingTest
    {
        public RandomChoiceTests(ITestOutputHelper output)
            : base(output)
        {
        }

        private class M : StateMachine
        {
            [Start]
            private class Init : State
            {
            }
        }

        [Fact(Timeout = 5000)]
        public void TestRandomChoice()
        {
            this.Test(r =>
            {
                if (r.RandomBoolean())
                {
                    r.CreateActor(typeof(M));
                }
            });
        }
    }
}
