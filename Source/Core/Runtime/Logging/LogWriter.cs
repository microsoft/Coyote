﻿// Copyright (c) Microsoft Corporation.
// Licensed under the MIT License.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Coyote.Actors;
using Microsoft.Coyote.Actors.Timers;
using Microsoft.Coyote.IO;
using Microsoft.Coyote.Runtime.Exploration;
using Microsoft.Coyote.Runtime.Logging;

namespace Microsoft.Coyote.Runtime
{
    /// <summary>
    /// Manages the installed <see cref="TextWriter"/> and all registered
    /// <see cref="IActorRuntimeLog"/> objects.
    /// </summary>
    internal sealed class LogWriter
    {
        /// <summary>
        /// The set of registered log writers.
        /// </summary>
        private readonly HashSet<IActorRuntimeLog> Logs;

        /// <summary>
        /// Used to log messages.
        /// </summary>
        internal TextWriter Logger { get; private set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="LogWriter"/> class.
        /// </summary>
        internal LogWriter(Configuration configuration)
        {
            this.Logs = new HashSet<IActorRuntimeLog>();

            if (configuration.IsVerbose)
            {
                this.GetOrCreateTextLog();
            }
            else
            {
                this.Logger = TextWriter.Null;
            }
        }

        /// <summary>
        /// Logs that the specified actor has been created.
        /// </summary>
        /// <param name="id">The id of the actor that has been created.</param>
        /// <param name="creator">The id of the creator, null otherwise.</param>
        public void LogCreateActor(ActorId id, ActorId creator)
        {
            if (this.Logs.Count > 0)
            {
                foreach (var log in this.Logs)
                {
                    log.OnCreateActor(id, creator);
                }
            }
        }

        /// <summary>
        /// Logs that the specified actor executes an action.
        /// </summary>
        /// <param name="id">The id of the actor executing the action.</param>
        /// <param name="stateName">The state name, if the actor is a state machine and a state exists, else null.</param>
        /// <param name="actionName">The name of the action being executed.</param>
        public void LogExecuteAction(ActorId id, string stateName, string actionName)
        {
            if (this.Logs.Count > 0)
            {
                foreach (var log in this.Logs)
                {
                    log.OnExecuteAction(id, stateName, actionName);
                }
            }
        }

        /// <summary>
        /// Logs that the specified event is sent to a target actor.
        /// </summary>
        /// <param name="targetActorId">The id of the target actor.</param>
        /// <param name="senderId">The id of the actor that sent the event, if any.</param>
        /// <param name="senderStateName">The state name, if the sender actor is a state machine and a state exists, else null.</param>
        /// <param name="e">The event being sent.</param>
        /// <param name="opGroupId">The id used to identify the send operation.</param>
        /// <param name="isTargetHalted">Is the target actor halted.</param>
        public void LogSendEvent(ActorId targetActorId, ActorId senderId, string senderStateName, Event e,
            Guid opGroupId, bool isTargetHalted)
        {
            if (this.Logs.Count > 0)
            {
                foreach (var log in this.Logs)
                {
                    log.OnSendEvent(targetActorId, senderId, senderStateName, e, opGroupId, isTargetHalted);
                }
            }
        }

        /// <summary>
        /// Logs that the specified actor raises an event.
        /// </summary>
        /// <param name="id">The id of the actor raising the event.</param>
        /// <param name="stateName">The state name, if the actor is a state machine and a state exists, else null.</param>
        /// <param name="e">The event being raised.</param>
        public void LogRaiseEvent(ActorId id, string stateName, Event e)
        {
            if (this.Logs.Count > 0)
            {
                foreach (var log in this.Logs)
                {
                    log.OnRaiseEvent(id, stateName, e);
                }
            }
        }

        /// <summary>
        /// Logs that the specified event is about to be enqueued to an actor.
        /// </summary>
        /// <param name="id">The id of the actor that the event is being enqueued to.</param>
        /// <param name="e">The event being enqueued.</param>
        public void LogEnqueueEvent(ActorId id, Event e)
        {
            if (this.Logs.Count > 0)
            {
                foreach (var log in this.Logs)
                {
                    log.OnEnqueueEvent(id, e);
                }
            }
        }

        /// <summary>
        /// Logs that the specified event is dequeued by an actor.
        /// </summary>
        /// <param name="id">The id of the actor that the event is being dequeued by.</param>
        /// <param name="stateName">The state name, if the actor is a state machine and a state exists, else null.</param>
        /// <param name="e">The event being dequeued.</param>
        public void LogDequeueEvent(ActorId id, string stateName, Event e)
        {
            if (this.Logs.Count > 0)
            {
                foreach (var log in this.Logs)
                {
                    log.OnDequeueEvent(id, stateName, e);
                }
            }
        }

        /// <summary>
        /// Logs that the specified event is received by an actor.
        /// </summary>
        /// <param name="id">The id of the actor that received the event.</param>
        /// <param name="stateName">The state name, if the actor is a state machine and a state exists, else null.</param>
        /// <param name="e">The event being received.</param>
        /// <param name="wasBlocked">The state machine was waiting for one or more specific events,
        /// and <paramref name="e"/> was one of them.</param>
        public void LogReceiveEvent(ActorId id, string stateName, Event e, bool wasBlocked)
        {
            if (this.Logs.Count > 0)
            {
                foreach (var log in this.Logs)
                {
                    log.OnReceiveEvent(id, stateName, e, wasBlocked);
                }
            }
        }

        /// <summary>
        /// Logs that the specified actor waits to receive an event of a specified type.
        /// </summary>
        /// <param name="id">The id of the actor that is entering the wait state.</param>
        /// <param name="stateName">The state name, if the actor is a state machine and a state exists, else null.</param>
        /// <param name="eventType">The type of the event being waited for.</param>
        public void LogWaitEvent(ActorId id, string stateName, Type eventType)
        {
            if (this.Logs.Count > 0)
            {
                foreach (var log in this.Logs)
                {
                    log.OnWaitEvent(id, stateName, eventType);
                }
            }
        }

        /// <summary>
        /// Logs that the specified actor waits to receive an event of one of the specified types.
        /// </summary>
        /// <param name="id">The id of the actor that is entering the wait state.</param>
        /// <param name="stateName">The state name, if the actor is a state machine and a state exists, else null.</param>
        /// <param name="eventTypes">The types of the events being waited for, if any.</param>
        public void LogWaitEvent(ActorId id, string stateName, params Type[] eventTypes)
        {
            if (this.Logs.Count > 0)
            {
                foreach (var log in this.Logs)
                {
                    log.OnWaitEvent(id, stateName, eventTypes);
                }
            }
        }

        /// <summary>
        /// Logs that the specified random result has been obtained.
        /// </summary>
        /// <param name="id">The id of the source actor, if any; otherwise, the runtime itself was the source.</param>
        /// <param name="result">The random result (may be bool or int).</param>
        public void LogRandom(ActorId id, object result)
        {
            if (this.Logs.Count > 0)
            {
                foreach (var log in this.Logs)
                {
                    log.OnRandom(id, result);
                }
            }
        }

        /// <summary>
        /// Logs that the specified state machine enters or exits a state.
        /// </summary>
        /// <param name="id">The id of the actor entering or exiting the state.</param>
        /// <param name="stateName">The name of the state being entered or exited.</param>
        /// <param name="isEntry">If true, this is called for a state entry; otherwise, exit.</param>
        public void LogStateTransition(ActorId id, string stateName, bool isEntry)
        {
            if (this.Logs.Count > 0)
            {
                foreach (var log in this.Logs)
                {
                    log.OnStateTransition(id, stateName, isEntry);
                }
            }
        }

        /// <summary>
        /// Logs that the specified state machine performs a goto state transition.
        /// </summary>
        /// <param name="id">The id of the actor.</param>
        /// <param name="currStateName">The name of the current state.</param>
        /// <param name="newStateName">The target state of the transition.</param>
        public void LogGotoState(ActorId id, string currStateName, string newStateName)
        {
            if (this.Logs.Count > 0)
            {
                foreach (var log in this.Logs)
                {
                    log.OnGotoState(id, currStateName, newStateName);
                }
            }
        }

        /// <summary>
        /// Logs that the specified state machine performs a push state transition.
        /// </summary>
        /// <param name="id">The id of the actor being pushed to the state.</param>
        /// <param name="currStateName">The name of the current state.</param>
        /// <param name="newStateName">The target state of the transition.</param>
        public void LogPushState(ActorId id, string currStateName, string newStateName)
        {
            if (this.Logs.Count > 0)
            {
                foreach (var log in this.Logs)
                {
                    log.OnPushState(id, currStateName, newStateName);
                }
            }
        }

        /// <summary>
        /// Logs that the specified state machine performs a pop state transition.
        /// </summary>
        /// <param name="id">The id of the actor that the pop executed in.</param>
        /// <param name="currStateName">The name of the current state.</param>
        /// <param name="restoredStateName">The name of the state being re-entered, if any.</param>
        public void LogPopState(ActorId id, string currStateName, string restoredStateName)
        {
            if (this.Logs.Count > 0)
            {
                foreach (var log in this.Logs)
                {
                    log.OnPopState(id, currStateName, restoredStateName);
                }
            }
        }

        /// <summary>
        /// Logs that the specified actor has halted.
        /// </summary>
        /// <param name="id">The id of the actor that has been halted.</param>
        /// <param name="inboxSize">Approximate size of the inbox.</param>
        public void LogHalt(ActorId id, int inboxSize)
        {
            if (this.Logs.Count > 0)
            {
                foreach (var log in this.Logs)
                {
                    log.OnHalt(id, inboxSize);
                }
            }
        }

        /// <summary>
        /// Logs that the specified actor is idle (there is nothing to dequeue) and the default
        /// event handler is about to be executed.
        /// </summary>
        /// <param name="id">The id of the actor that the state will execute in.</param>
        /// <param name="stateName">The state name, if the actor is a state machine and a state exists, else null.</param>
        public void LogDefaultEventHandler(ActorId id, string stateName)
        {
            if (this.Logs.Count > 0)
            {
                foreach (var log in this.Logs)
                {
                    log.OnDefaultEventHandler(id, stateName);
                }
            }
        }

        /// <summary>
        /// Logs that the specified state machine handled a raised event.
        /// </summary>
        /// <param name="id">The id of the actor handling the event.</param>
        /// <param name="stateName">The name of the current state.</param>
        /// <param name="e">The event being handled.</param>
        public void LogHandleRaisedEvent(ActorId id, string stateName, Event e)
        {
            if (this.Logs.Count > 0)
            {
                foreach (var log in this.Logs)
                {
                    log.OnHandleRaisedEvent(id, stateName, e);
                }
            }
        }

        /// <summary>
        /// Logs that the specified event cannot be handled in the current state, its exit
        /// handler is executed and then the state is popped and any previous "current state"
        /// is reentered. This handler is called when that pop has been done.
        /// </summary>
        /// <param name="id">The id of the actor that the pop executed in.</param>
        /// <param name="stateName">The state name, if the actor is a state machine and a state exists, else null.</param>
        /// <param name="e">The event that cannot be handled.</param>
        public void LogPopUnhandledEvent(ActorId id, string stateName, Event e)
        {
            if (this.Logs.Count > 0)
            {
                foreach (var log in this.Logs)
                {
                    log.OnPopUnhandledEvent(id, stateName, e);
                }
            }
        }

        /// <summary>
        /// Logs that the specified actor throws an exception.
        /// </summary>
        /// <param name="id">The id of the actor that threw the exception.</param>
        /// <param name="stateName">The state name, if the actor is a state machine and a state exists, else null.</param>
        /// <param name="actionName">The name of the action being executed.</param>
        /// <param name="ex">The exception.</param>
        public void LogExceptionThrown(ActorId id, string stateName, string actionName, Exception ex)
        {
            if (this.Logs.Count > 0)
            {
                foreach (var log in this.Logs)
                {
                    log.OnExceptionThrown(id, stateName, actionName, ex);
                }
            }
        }

        /// <summary>
        /// Logs that the specified OnException method is used to handle a thrown exception.
        /// </summary>
        /// <param name="id">The id of the actor that threw the exception.</param>
        /// <param name="stateName">The state name, if the actor is a state machine and a state exists, else null.</param>
        /// <param name="actionName">The name of the action being executed.</param>
        /// <param name="ex">The exception.</param>
        public void LogExceptionHandled(ActorId id, string stateName, string actionName, Exception ex)
        {
            if (this.Logs.Count > 0)
            {
                foreach (var log in this.Logs)
                {
                    log.OnExceptionHandled(id, stateName, actionName, ex);
                }
            }
        }

        /// <summary>
        /// Logs that the specified actor timer has been created.
        /// </summary>
        /// <param name="info">Handle that contains information about the timer.</param>
        public void LogCreateTimer(TimerInfo info)
        {
            if (this.Logs.Count > 0)
            {
                foreach (var log in this.Logs)
                {
                    log.OnCreateTimer(info);
                }
            }
        }

        /// <summary>
        /// Logs that the specified actor timer has been stopped.
        /// </summary>
        /// <param name="info">Handle that contains information about the timer.</param>
        public void LogStopTimer(TimerInfo info)
        {
            if (this.Logs.Count > 0)
            {
                foreach (var log in this.Logs)
                {
                    log.OnStopTimer(info);
                }
            }
        }

        /// <summary>
        /// Logs that the specified monitor has been created.
        /// </summary>
        /// <param name="monitorTypeName">The name of the type of the monitor that has been created.</param>
        /// <param name="id">The id of the monitor that has been created.</param>
        public void LogCreateMonitor(string monitorTypeName, ActorId id)
        {
            if (this.Logs.Count > 0)
            {
                foreach (var log in this.Logs)
                {
                    log.OnCreateMonitor(monitorTypeName, id);
                }
            }
        }

        /// <summary>
        /// Logs that the specified monitor executes an action.
        /// </summary>
        /// <param name="monitorTypeName">Name of type of the monitor that is executing the action.</param>
        /// <param name="id">The id of the monitor that is executing the action</param>
        /// <param name="stateName">The name of the state in which the action is being executed.</param>
        /// <param name="actionName">The name of the action being executed.</param>
        public void LogMonitorExecuteAction(string monitorTypeName, ActorId id, string stateName, string actionName)
        {
            if (this.Logs.Count > 0)
            {
                foreach (var log in this.Logs)
                {
                    log.OnMonitorExecuteAction(monitorTypeName, id, stateName, actionName);
                }
            }
        }

        /// <summary>
        /// Logs that the specified monitor is about to process an event.
        /// </summary>
        /// <param name="senderId">The sender of the event.</param>
        /// <param name="senderStateName">The name of the state the sender is in.</param>
        /// <param name="monitorTypeName">Name of type of the monitor that will process the event.</param>
        /// <param name="id">The id of the monitor that will process the event.</param>
        /// <param name="stateName">The name of the state in which the event is being raised.</param>
        /// <param name="e">The event being processed.</param>
        public void LogMonitorProcessEvent(ActorId senderId, string senderStateName, string monitorTypeName,
            ActorId id, string stateName, Event e)
        {
            if (this.Logs.Count > 0)
            {
                foreach (var log in this.Logs)
                {
                    log.OnMonitorProcessEvent(senderId, senderStateName, monitorTypeName, id, stateName, e);
                }
            }
        }

        /// <summary>
        /// Logs that the specified monitor raised an event.
        /// </summary>
        /// <param name="monitorTypeName">Name of type of the monitor raising the event.</param>
        /// <param name="id">The id of the monitor raising the event.</param>
        /// <param name="stateName">The name of the state in which the event is being raised.</param>
        /// <param name="e">The event being raised.</param>
        public void LogMonitorRaiseEvent(string monitorTypeName, ActorId id, string stateName, Event e)
        {
            if (this.Logs.Count > 0)
            {
                foreach (var log in this.Logs)
                {
                    log.OnMonitorRaiseEvent(monitorTypeName, id, stateName, e);
                }
            }
        }

        /// <summary>
        /// Logs that the specified monitor enters or exits a state.
        /// </summary>
        /// <param name="monitorTypeName">The name of the type of the monitor entering or exiting the state</param>
        /// <param name="id">The id of the monitor entering or exiting the state</param>
        /// <param name="stateName">The name of the state being entered or exited; if <paramref name="isInHotState"/>
        /// is not null, then the temperature is appended to the statename in brackets, e.g. "stateName[hot]".</param>
        /// <param name="isEntry">If true, this is called for a state entry; otherwise, exit.</param>
        /// <param name="isInHotState">If true, the monitor is in a hot state; if false, the monitor is in a cold state;
        /// else no liveness state is available.</param>
        public void LogMonitorStateTransition(string monitorTypeName, ActorId id, string stateName,
            bool isEntry, bool? isInHotState)
        {
            if (this.Logs.Count > 0)
            {
                foreach (var log in this.Logs)
                {
                    log.OnMonitorStateTransition(monitorTypeName, id, stateName, isEntry, isInHotState);
                }
            }
        }

        /// <summary>
        /// Logs that the specified assertion failure has occurred.
        /// </summary>
        /// <param name="error">The text of the error.</param>
        public void LogAssertionFailure(string error)
        {
            if (this.Logs.Count > 0)
            {
                foreach (var log in this.Logs)
                {
                    log.OnAssertionFailure(error);
                }
            }
        }

        /// <summary>
        /// Logs the specified scheduling strategy description.
        /// </summary>
        /// <param name="strategy">The scheduling strategy that was used.</param>
        /// <param name="description">More information about the scheduling strategy.</param>
        public void LogStrategyDescription(SchedulingStrategy strategy, string description)
        {
            if (this.Logs.Count > 0)
            {
                foreach (var log in this.Logs)
                {
                    log.OnStrategyDescription(strategy, description);
                }
            }
        }

        /// <summary>
        /// Use this method to notify all logs that the test iteration is complete.
        /// </summary>
        internal void LogCompletion()
        {
            foreach (var log in this.Logs)
            {
                log.OnCompleted();
            }
        }

        /// <summary>
        /// Returns all registered logs of type <typeparamref name="TActorRuntimeLog"/>,
        /// if there are any.
        /// </summary>
        public IEnumerable<TActorRuntimeLog> GetLogsOfType<TActorRuntimeLog>()
            where TActorRuntimeLog : IActorRuntimeLog =>
            this.Logs.OfType<TActorRuntimeLog>();

        /// <summary>
        /// Use this method to override the default <see cref="TextWriter"/> for logging messages.
        /// </summary>
        internal TextWriter SetLogger(TextWriter logger)
        {
            var prevLogger = this.Logger;
            if (logger == null)
            {
                this.Logger = TextWriter.Null;

                var textLog = this.GetLogsOfType<ActorRuntimeLogTextFormatter>().FirstOrDefault();
                if (textLog != null)
                {
                    textLog.Logger = this.Logger;
                }
            }
            else
            {
                this.Logger = logger;

                // This overrides the original IsVerbose flag and creates a text logger anyway!
                var textLog = this.GetOrCreateTextLog();
                textLog.Logger = this.Logger;
            }

            return prevLogger;
        }

        private ActorRuntimeLogTextFormatter GetOrCreateTextLog()
        {
            var textLog = this.GetLogsOfType<ActorRuntimeLogTextFormatter>().FirstOrDefault();
            if (textLog == null)
            {
                if (this.Logger == null)
                {
                    this.Logger = new ConsoleLogger();
                }

                textLog = new ActorRuntimeLogTextFormatter();
                textLog.Logger = this.Logger;
                this.Logs.Add(textLog);
            }

            return textLog;
        }

        /// <summary>
        /// Use this method to register an <see cref="IActorRuntimeLog"/>.
        /// </summary>
        internal void RegisterLog(IActorRuntimeLog log)
        {
            if (log == null)
            {
                throw new InvalidOperationException("Cannot register a null log.");
            }

            // Make sure we only have one text logger
            if (log is ActorRuntimeLogTextFormatter a)
            {
                var textLog = this.GetLogsOfType<ActorRuntimeLogTextFormatter>().FirstOrDefault();
                if (textLog != null)
                {
                    this.Logs.Remove(textLog);
                }

                if (this.Logger != null)
                {
                    a.Logger = this.Logger;
                }
            }

            this.Logs.Add(log);
        }

        /// <summary>
        /// Use this method to unregister a previously registered <see cref="IActorRuntimeLog"/>.
        /// </summary>
        internal void RemoveLog(IActorRuntimeLog log)
        {
            if (log != null)
            {
                this.Logs.Remove(log);
            }
        }
    }
}
