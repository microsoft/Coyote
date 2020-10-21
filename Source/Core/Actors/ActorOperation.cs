﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using Microsoft.Coyote.SystematicTesting;

namespace Microsoft.Coyote.Actors
{
    /// <summary>
    /// Contains information about an asynchronous actor operation
    /// that can be controlled during testing.
    /// </summary>
    internal sealed class ActorOperation : TaskOperation
    {
        /// <summary>
        /// The actor that executes this operation.
        /// </summary>
        internal readonly Actor Actor;

        /// <summary>
        /// Set of events that this operation is waiting to receive. Receiving
        /// any event in the set allows this operation to resume.
        /// </summary>
        private readonly HashSet<Type> EventDependencies;

        /// <summary>
        /// True if it should skip the next receive scheduling point,
        /// because it was already called in the end of the previous
        /// event handler.
        /// </summary>
        internal bool SkipNextReceiveSchedulingPoint;

        /// <summary>
        /// Initializes a new instance of the <see cref="ActorOperation"/> class.
        /// </summary>
        internal ActorOperation(Actor actor, OperationScheduler scheduler)
            : base(actor.Id.Value, actor.Id.Name, scheduler)
        {
            this.Actor = actor;
            this.EventDependencies = new HashSet<Type>();
            this.SkipNextReceiveSchedulingPoint = false;
        }

        /// <summary>
        /// Invoked when the operation is waiting to receive an event of the specified type or types.
        /// </summary>
        internal void OnWaitEvent(IEnumerable<Type> eventTypes)
        {
            this.EventDependencies.UnionWith(eventTypes);
            this.Status = AsyncOperationStatus.BlockedOnReceive;
        }

        /// <summary>
        /// Invoked when the operation received an event from the specified operation.
        /// </summary>
        internal void OnReceivedEvent()
        {
            this.EventDependencies.Clear();
            this.Status = AsyncOperationStatus.Enabled;
        }

        /// <summary>
        /// Invoked when the operation completes.
        /// </summary>
        internal override void OnCompleted()
        {
            this.SkipNextReceiveSchedulingPoint = true;
            base.OnCompleted();
        }
    }
}
