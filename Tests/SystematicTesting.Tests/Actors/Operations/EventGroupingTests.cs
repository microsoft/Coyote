﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using Xunit.Abstractions;

namespace Microsoft.Coyote.SystematicTesting.Tests.Actors
{
    public class EventGroupingTests : Microsoft.Coyote.Actors.Tests.Actors.EventGroupingTests
    {
        public EventGroupingTests(ITestOutputHelper output)
            : base(output)
        {
        }

        public override bool IsSystematicTest => true;
    }
}
