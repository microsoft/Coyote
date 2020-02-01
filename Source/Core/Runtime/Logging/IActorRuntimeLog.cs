﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using Microsoft.Coyote.Actors;
using Microsoft.Coyote.Actors.Timers;
using Microsoft.Coyote.Runtime.Exploration;

namespace Microsoft.Coyote.Runtime
{
    /// <summary>
    /// Interface that allows an external module to track what
    /// is happening in the <see cref="IActorRuntime"/>.
    /// </summary>
    /// <remarks>
    /// See <see href="/coyote/learn/advanced/logging" >Logging</see> for more information.
    /// </remarks>
    public interface IActorRuntimeLog
    {
        /// <summary>
        /// Invoked when the specified actor has been created.
        /// </summary>
        /// <param name="id">The id of the actor that has been created.</param>
        /// <param name="creator">The id of the creator, or null.</param>
        void OnCreateActor(ActorId id, ActorId creator);

        /// <summary>
        /// Invoked when the specified actor executes an action.
        /// </summary>
        /// <param name="id">The id of the actor executing the action.</param>
        /// <param name="stateName">The state name, if the actor is a state machine and a state exists, else null.</param>
        /// <param name="actionName">The name of the action being executed.</param>
        void OnExecuteAction(ActorId id, string stateName, string actionName);

        /// <summary>
        /// Invoked when the specified event is sent to a target actor.
        /// </summary>
        /// <param name="targetActorId">The id of the target actor.</param>
        /// <param name="senderId">The id of the actor that sent the event, if any.</param>
        /// <param name="senderStateName">The state name, if the sender actor is a state machine and a state exists, else null.</param>
        /// <param name="e">The event being sent.</param>
        /// <param name="opGroupId">The id used to identify the send operation.</param>
        /// <param name="isTargetHalted">Is the target actor halted.</param>
        void OnSendEvent(ActorId targetActorId, ActorId senderId, string senderStateName, Event e,
            Guid opGroupId, bool isTargetHalted);

        /// <summary>
        /// Invoked when the specified state machine raises an event.
        /// </summary>
        /// <param name="id">The id of the actor raising the event.</param>
        /// <param name="stateName">The name of the current state.</param>
        /// <param name="e">The event being raised.</param>
        void OnRaiseEvent(ActorId id, string stateName, Event e);

        /// <summary>
        /// Invoked when the specified event is about to be enqueued to an actor.
        /// </summary>
        /// <param name="id">The id of the actor that the event is being enqueued to.</param>
        /// <param name="e">The event being enqueued.</param>
        void OnEnqueueEvent(ActorId id, Event e);

        /// <summary>
        /// Invoked when the specified event is dequeued by an actor.
        /// </summary>
        /// <param name="id">The id of the actor that the event is being dequeued by.</param>
        /// <param name="stateName">The state name, if the actor is a state machine and a state exists, else null.</param>
        /// <param name="e">The event being dequeued.</param>
        void OnDequeueEvent(ActorId id, string stateName, Event e);

        /// <summary>
        /// Invoked when the specified event is received by an actor.
        /// </summary>
        /// <param name="id">The id of the actor that received the event.</param>
        /// <param name="stateName">The state name, if the actor is a state machine and a state exists, else null.</param>
        /// <param name="e">The the event being received.</param>
        /// <param name="wasBlocked">The actor was waiting for one or more specific events,
        /// and <paramref name="e"/> was one of them</param>
        void OnReceiveEvent(ActorId id, string stateName, Event e, bool wasBlocked);

        /// <summary>
        /// Invoked when the specified actor waits to receive an event of a specified type.
        /// </summary>
        /// <param name="id">The id of the actor that is entering the wait state.</param>
        /// <param name="stateName">The state name, if the actor is a state machine and a state exists, else null.</param>
        /// <param name="eventType">The type of the event being waited for.</param>
        void OnWaitEvent(ActorId id, string stateName, Type eventType);

        /// <summary>
        /// Invoked when the specified actor waits to receive an event of one of the specified types.
        /// </summary>
        /// <param name="id">The id of the actor that is entering the wait state.</param>
        /// <param name="stateName">The state name, if the actor is a state machine and a state exists, else null.</param>
        /// <param name="eventTypes">The types of the events being waited for, if any.</param>
        void OnWaitEvent(ActorId id, string stateName, params Type[] eventTypes);

        /// <summary>
        /// Invoked when the specified random result has been obtained.
        /// </summary>
        /// <param name="id">The id of the source actor, if any; otherwise, the runtime itself was the source.</param>
        /// <param name="result">The random result (may be bool or int).</param>
        void OnRandom(ActorId id, object result);

        /// <summary>
        /// Invoked when the specified state machine enters or exits a state.
        /// </summary>
        /// <param name="id">The id of the actor entering or exiting the state.</param>
        /// <param name="stateName">The name of the state being entered or exited.</param>
        /// <param name="isEntry">If true, this is called for a state entry; otherwise, exit.</param>
        void OnStateTransition(ActorId id, string stateName, bool isEntry);

        /// <summary>
        /// Invoked when the specified state machine performs a goto transition to the specified state.
        /// </summary>
        /// <param name="id">The id of the actor.</param>
        /// <param name="currStateName">The name of the current state.</param>
        /// <param name="newStateName">The target state of the transition.</param>
        void OnGotoState(ActorId id, string currStateName, string newStateName);

        /// <summary>
        /// Invoked when the specified state machine is being pushed to a state.
        /// </summary>
        /// <param name="id">The id of the actor being pushed to the state.</param>
        /// <param name="currStateName">The name of the current state.</param>
        /// <param name="newStateName">The target state of the transition.</param>
        void OnPushState(ActorId id, string currStateName, string newStateName);

        /// <summary>
        /// Invoked when the specified state machine has popped its current state.
        /// </summary>
        /// <param name="id">The id of the actor that the pop executed in.</param>
        /// <param name="currStateName">The name of the current state.</param>
        /// <param name="restoredStateName">The name of the state being re-entered, if any.</param>
        void OnPopState(ActorId id, string currStateName, string restoredStateName);

        /// <summary>
        /// Invoked when the specified actor is idle (there is nothing to dequeue) and the default
        /// event handler is about to be executed.
        /// </summary>
        /// <param name="id">The id of the actor that the state will execute in.</param>
        /// <param name="stateName">The state name, if the actor is a state machine and a state exists, else null.</param>
        void OnDefaultEventHandler(ActorId id, string stateName);

        /// <summary>
        /// Invoked when the specified actor has been halted.
        /// </summary>
        /// <param name="id">The id of the actor that has been halted.</param>
        /// <param name="inboxSize">Approximate size of the inbox.</param>
        void OnHalt(ActorId id, int inboxSize);

        /// <summary>
        /// Invoked when the specified actor handled a raised event.
        /// </summary>
        /// <param name="id">The id of the actor handling the event.</param>
        /// <param name="stateName">The state name, if the actor is a state machine and a state exists, else null.</param>
        /// <param name="e">The event being handled.</param>
        void OnHandleRaisedEvent(ActorId id, string stateName, Event e);

        /// <summary>
        /// Invoked when the specified event cannot be handled in the current state, its exit
        /// handler is executed and then the state is popped and any previous "current state"
        /// is reentered. This handler is called when that pop has been done.
        /// </summary>
        /// <param name="id">The id of the actor that the pop executed in.</param>
        /// <param name="stateName">The state name, if the actor is a state machine and a state exists, else null.</param>
        /// <param name="e">The event that cannot be handled.</param>
        void OnPopUnhandledEvent(ActorId id, string stateName, Event e);

        /// <summary>
        /// Invoked when the specified actor throws an exception.
        /// </summary>
        /// <param name="id">The id of the actor that threw the exception.</param>
        /// <param name="stateName">The state name, if the actor is a state machine and a state exists, else null.</param>
        /// <param name="actionName">The name of the action being executed.</param>
        /// <param name="ex">The exception.</param>
        void OnExceptionThrown(ActorId id, string stateName, string actionName, Exception ex);

        /// <summary>
        /// Invoked when the specified OnException method is used to handle a thrown exception.
        /// </summary>
        /// <param name="id">The id of the actor that threw the exception.</param>
        /// <param name="stateName">The state name, if the actor is a state machine and a state exists, else null.</param>
        /// <param name="actionName">The name of the action being executed.</param>
        /// <param name="ex">The exception.</param>
        void OnExceptionHandled(ActorId id, string stateName, string actionName, Exception ex);

        /// <summary>
        /// Invoked when the specified actor timer has been created.
        /// </summary>
        /// <param name="info">Handle that contains information about the timer.</param>
        void OnCreateTimer(TimerInfo info);

        /// <summary>
        /// Invoked when the specified actor timer has been stopped.
        /// </summary>
        /// <param name="info">Handle that contains information about the timer.</param>
        void OnStopTimer(TimerInfo info);

        /// <summary>
        /// Invoked when the specified monitor has been created.
        /// </summary>
        /// <param name="monitorTypeName">The name of the type of the monitor that has been created.</param>
        /// <param name="id">The id of the monitor that has been created.</param>
        void OnCreateMonitor(string monitorTypeName, ActorId id);

        /// <summary>
        /// Invoked when the specified monitor executes an action.
        /// </summary>
        /// <param name="monitorTypeName">Name of type of the monitor that is executing the action.</param>
        /// <param name="id">The id of the monitor that is executing the action</param>
        /// <param name="stateName">The name of the state in which the action is being executed.</param>
        /// <param name="actionName">The name of the action being executed.</param>
        void OnMonitorExecuteAction(string monitorTypeName, ActorId id, string stateName, string actionName);

        /// <summary>
        /// Invoked when the specified monitor is about to process an event.
        /// </summary>
        /// <param name="senderId">The sender of the event.</param>
        /// <param name="senderStateName">The name of the state the sender is in.</param>
        /// <param name="monitorTypeName">Name of type of the monitor that will process the event.</param>
        /// <param name="id">The id of the monitor that will process the event.</param>
        /// <param name="stateName">The name of the state in which the event is being raised.</param>
        /// <param name="e">The event being processed.</param>
        void OnMonitorProcessEvent(ActorId senderId, string senderStateName, string monitorTypeName, ActorId id,
            string stateName, Event e);

        /// <summary>
        /// Invoked when the specified monitor raised an event.
        /// </summary>
        /// <param name="monitorTypeName">Name of type of the monitor raising the event.</param>
        /// <param name="id">The id of the monitor raising the event.</param>
        /// <param name="stateName">The name of the state in which the event is being raised.</param>
        /// <param name="e">The event being raised.</param>
        void OnMonitorRaiseEvent(string monitorTypeName, ActorId id, string stateName, Event e);

        /// <summary>
        /// Invoked when the specified monitor enters or exits a state.
        /// </summary>
        /// <param name="monitorTypeName">The name of the type of the monitor entering or exiting the state</param>
        /// <param name="id">The id of the monitor entering or exiting the state</param>
        /// <param name="stateName">The name of the state being entered or exited; if <paramref name="isInHotState"/>
        /// is not null, then the temperature is appended to the statename in brackets, e.g. "stateName[hot]".</param>
        /// <param name="isEntry">If true, this is called for a state entry; otherwise, exit.</param>
        /// <param name="isInHotState">If true, the monitor is in a hot state; if false, the monitor is in a cold state;
        /// else no liveness state is available.</param>
        void OnMonitorStateTransition(string monitorTypeName, ActorId id, string stateName,
            bool isEntry, bool? isInHotState);

        /// <summary>
        /// Invoked when the specified assertion failure has occurred.
        /// </summary>
        /// <param name="error">The text of the error.</param>
        void OnAssertionFailure(string error);

        /// <summary>
        /// Invoked to describe the specified scheduling strategy.
        /// </summary>
        /// <param name="strategy">The scheduling strategy that was used.</param>
        /// <param name="description">More information about the scheduling strategy.</param>
        void OnStrategyDescription(SchedulingStrategy strategy, string description);

        /// <summary>
        /// Invoked when a log is complete (and is about to be closed).
        /// </summary>
        void OnCompleted();
    }
}
