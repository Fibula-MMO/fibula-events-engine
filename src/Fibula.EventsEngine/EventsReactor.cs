// -----------------------------------------------------------------
// <copyright file="EventsReactor.cs" company="2Dudes">
// Copyright (c) | Jose L. Nunez de Caceres et al.
// https://linkedin.com/in/nunezdecaceres
//
// All Rights Reserved.
//
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------

namespace Fibula.EventsEngine;

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Fibula.EventsEngine.Contracts.Abstractions;
using Fibula.EventsEngine.Contracts.Delegates;
using Fibula.EventsEngine.Contracts.Enumerations;
using Fibula.Utilities.Common.Extensions;
using Fibula.Utilities.Validation;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

/// <summary>
/// Class that represents a scheduler for events.
/// </summary>
public class EventsReactor : IEventsReactor<Event>
{
    /// <summary>
    /// A lock object to monitor when new events are added to the queue.
    /// </summary>
    private readonly object eventsAvailableLock;

    /// <summary>
    /// A lock object to monitor when new events are added to the demultiplexing queue.
    /// </summary>
    private readonly object eventsPushedLock;

    /// <summary>
    /// The internal queue used to feed events to the loop.
    /// </summary>
    private readonly LinkedList<EventsNode> eventsQueue;

    /// <summary>
    /// Internal queue to handle asynchronous event demultiplexing.
    /// </summary>
    private readonly Queue<(Event evt, DateTimeOffset requestTime, TimeSpan requestedDelay)> eventDemultiplexingQueue;

    /// <summary>
    /// A mapping of events ids to the actual event object, for O(1) lookup.
    /// </summary>
    private readonly Dictionary<Guid, Event> eventsIndex;

    /// <summary>
    /// A mapping of event ids to the node they are contained in, for O(1) lookup.
    /// </summary>
    private readonly Dictionary<Guid, EventsNode> eventNodesIndex;

    /// <summary>
    /// The time to round events target time by.
    /// </summary>
    private readonly TimeSpan timeToRoundBy;

    /// <summary>
    /// Initializes a new instance of the <see cref="EventsReactor"/> class.
    /// </summary>
    /// <param name="logger">The logger to use.</param>
    /// <param name="reactorOptions">The options for the reactor.</param>
    public EventsReactor(ILogger<EventsReactor> logger, IOptions<EventsReactorOptions> reactorOptions)
    {
        logger.ThrowIfNull(nameof(logger));
        reactorOptions.ThrowIfNull(nameof(reactorOptions));

        DataAnnotationsValidator.ValidateObjectRecursive(reactorOptions.Value);

        this.Logger = logger;

        this.eventsAvailableLock = new object();
        this.eventsPushedLock = new object();

        this.eventsQueue = new LinkedList<EventsNode>();
        this.eventDemultiplexingQueue = new Queue<(Event evt, DateTimeOffset requestTime, TimeSpan requestedDelay)>();
        this.eventsIndex = new Dictionary<Guid, Event>();
        this.eventNodesIndex = new Dictionary<Guid, EventsNode>();
        this.timeToRoundBy = TimeSpan.FromMilliseconds(reactorOptions.Value.EventRoundByMilliseconds.Value);
    }

    /// <summary>
    /// Event fired when the reactor has an event ready to process.
    /// </summary>
    public event EventReadyDelegate EventReady;

    /// <summary>
    /// Gets a reference to the logger instance.
    /// </summary>
    public ILogger Logger { get; }

    /// <summary>
    /// Gets the number of events in the queue.
    /// </summary>
    public int QueueSize => this.eventsQueue.Count;

    /// <summary>
    /// Gets the current time.
    /// </summary>
    private static DateTimeOffset CurrentTime => DateTimeOffset.UtcNow;

    /// <summary>
    /// Cancels an event.
    /// </summary>
    /// <param name="evtId">The id of the event to cancel.</param>
    /// <returns>True if the event was successfully cancelled, and false otherwise.</returns>
    public bool Cancel(Guid evtId)
    {
        lock (this.eventsAvailableLock)
        {
            if (!this.eventsIndex.TryGetValue(evtId, out Event evt))
            {
                this.Logger.LogTrace("Unable to cancel event: An event with id {eventId} was not found in the index.", evtId);

                return false;
            }

            return this.Cancel(evt);
        }
    }

    /// <summary>
    /// Attempts to delay this event, pushing its next ready time into the future.
    /// </summary>
    /// <param name="evtId">The id of the event to delay.</param>
    /// <param name="delayBy">The amount of time to delay the event by.</param>
    /// <returns>True if the event is successfully delayed, and false otherwise.</returns>
    public bool Delay(Guid evtId, TimeSpan delayBy)
    {
        lock (this.eventsAvailableLock)
        {
            if (!this.eventsIndex.TryGetValue(evtId, out Event evt))
            {
                this.Logger.LogTrace("Unable to delay event: An event with id {eventId} was not found in the index.", evtId);

                return false;
            }

            return this.Delay(evt, delayBy);
        }
    }

    /// <summary>
    /// Attempts to hurry this event, decreasing the time for it to be marked ready.
    /// </summary>
    /// <param name="evtId">The id of the event to hurry.</param>
    /// <param name="hurryBy">The amount of time to hurry the event by.</param>
    /// <returns>True if the event is successfully hurried, and false otherwise.</returns>
    public bool Hurry(Guid evtId, TimeSpan hurryBy)
    {
        lock (this.eventsAvailableLock)
        {
            if (!this.eventsIndex.TryGetValue(evtId, out Event evt))
            {
                this.Logger.LogTrace("Unable to hurry event: An event with id {eventId} was not found in the index.", evtId);

                return false;
            }

            return this.Hurry(evt, hurryBy);
        }
    }

    /// <summary>
    /// Attempts to expedite this event to the front of the queue.
    /// </summary>
    /// <param name="evtId">The id of the event to expedite.</param>
    /// <returns>True if the event is successfully expedited, and false otherwise.</returns>
    public bool Expedite(Guid evtId)
    {
        lock (this.eventsAvailableLock)
        {
            if (!this.eventsIndex.TryGetValue(evtId, out Event evt))
            {
                this.Logger.LogTrace("Unable to expedite event: An event with id {eventId} was not found in the index.", evtId);

                return false;
            }

            return this.Hurry(evt, TimeSpan.MaxValue);
        }
    }

    /// <summary>
    /// Pushes an event into the reactor, asynchronously.
    /// </summary>
    /// <param name="eventToPush">The event to push.</param>
    /// <param name="delayBy">Optional. A delay after which the event should be fired. If left null, the event is scheduled to be fired ASAP.</param>
    public void Push(Event eventToPush, TimeSpan? delayBy = null)
    {
        eventToPush.ThrowIfNull(nameof(eventToPush));

        if (delayBy == null || delayBy < TimeSpan.Zero)
        {
            delayBy = TimeSpan.Zero;
        }

        lock (this.eventsPushedLock)
        {
            this.eventDemultiplexingQueue.Enqueue((eventToPush, CurrentTime, delayBy.Value));

            this.Logger.LogTrace("Pushed event {eventName} with id {eventId} for demultiplexing.", eventToPush.GetType().Name, eventToPush.Id);

            Monitor.Pulse(this.eventsPushedLock);
        }
    }

    /// <summary>
    /// Starts the reactor.
    /// </summary>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous processing operation.</returns>
    public Task RunAsync(CancellationToken cancellationToken)
    {
        return Task.WhenAll(this.EventsProcessingLoop(cancellationToken), this.DemultiplexingEventsLoop(cancellationToken));
    }

    /// <summary>
    /// Starts the reactor's event processing loop.
    /// </summary>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous processing operation.</returns>
    private Task EventsProcessingLoop(CancellationToken cancellationToken)
    {
        return Task.Run(
            () =>
            {
                this.Logger.LogDebug("Events reactor processing loop started.");

                // Max timeout accepted in Monitor.Wait is 2^31 - 1 milliseconds.
                var maxTimeout = TimeSpan.FromMilliseconds(int.MaxValue);
                var timeout = maxTimeout;
                var sw = new Stopwatch();

                long cyclesProcessed = 0;
                long cycleTimeTotal = 0;

                while (!cancellationToken.IsCancellationRequested)
                {
                    lock (this.eventsAvailableLock)
                    {
                        // Normalize to zero because the Monitor.Wait() call throws on negative values.
                        if (timeout < TimeSpan.Zero)
                        {
                            timeout = TimeSpan.Zero;
                        }

                        // Wait until we're flagged that there are events available.
                        var prettyTimeDescription = timeout == maxTimeout ? "indefinitely" : timeout.ToString();
                        this.Logger.LogTrace("Processor will wait {time}.", prettyTimeDescription);

                        Monitor.Wait(this.eventsAvailableLock, timeout);

                        // Then, we reset time to wait and start the stopwatch.
                        timeout = maxTimeout;
                        sw.Restart();

                        if (!this.eventsQueue.Any())
                        {
                            // no more items on the queue, go to wait.
                            this.Logger.LogWarning("The event processing loop was woken up but the events queue was empty.");
                            continue;
                        }

                        // Maintain the average processing time to adjust the decision whether the next event is
                        // within the execution time epsilon.
                        var avgProcessingTime = cyclesProcessed == 0 ? 0 : cycleTimeTotal / cyclesProcessed++;

                        // Check the current queue and fire any events that are due.
                        while (this.IsNextEventDue(TimeSpan.FromMilliseconds(avgProcessingTime), ref timeout) && this.DequeueEventNode() is EventsNode nextNode)
                        {
                            foreach (var evt in nextNode.Events)
                            {
                                this.eventsIndex.Remove(evt.Id);
                                this.eventNodesIndex.Remove(evt.Id);

                                if (evt.State == EventState.Cancelled)
                                {
                                    this.Logger.LogTrace("Event {eventName} with id {eventId} ignored since it was cancelled.", evt.GetType().Name, evt.Id);
                                    continue;
                                }

                                var currentTime = CurrentTime;

                                this.Logger.LogTrace("Event {eventName} with id {eventId}, marked ready at {currentTimeTicks}.", evt.GetType().Name, evt.Id, currentTime.Ticks);

                                evt.State = EventState.Executing;

                                this.EventReady?.Invoke(this, evt, currentTime - evt.NextReadyTime.Value);

                                evt.State = EventState.Executed;
                                evt.NextReadyTime = null;
                            }
                        }

                        sw.Stop();

                        cycleTimeTotal += sw.ElapsedMilliseconds;
                    }
                }

                this.Logger.LogDebug("Events reactor processing loop stopped.");
            },
            cancellationToken);
    }

    /// <summary>
    /// Starts the reactor's event demultiplexing loop.
    /// </summary>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous processing operation.</returns>
    private Task DemultiplexingEventsLoop(CancellationToken cancellationToken)
    {
        return Task.Run(
              () =>
              {
                  this.Logger.LogDebug("Demultiplexing events loop started.");

                  while (!cancellationToken.IsCancellationRequested)
                  {
                      lock (this.eventsPushedLock)
                      {
                          this.Logger.LogTrace("Demultiplexor is now waiting for more events.");

                          Monitor.Wait(this.eventsPushedLock);

                          lock (this.eventsAvailableLock)
                          {
                              // Since we have locked on the events lock, we know that the processing loop is halted.
                              // Let's add the events waiting to be added to the queue as fast as possible.
                              while (this.eventDemultiplexingQueue.TryDequeue(out (Event Event, DateTimeOffset RequestedAt, TimeSpan Delay) request))
                              {
                                  var currentTime = CurrentTime;
                                  var elapsedTime = currentTime - request.RequestedAt;
                                  var targetTime = currentTime + request.Delay - elapsedTime;

                                  this.EnqueueEvent(request.Event, ref targetTime);

                                  this.Logger.LogTrace("Enqueued {eventName} with id {eventId}, due in {delayInMs} milliseconds (at {targetTimeTicks}).", request.Event.GetType().Name, request.Event.Id, request.Delay.TotalMilliseconds, targetTime.Ticks);
                              }

                              // Let the other loop know there's more work available.
                              if (this.eventsQueue.Any())
                              {
                                  Monitor.Pulse(this.eventsAvailableLock);
                              }
                          }
                      }
                  }

                  this.Logger.LogDebug("Demultiplexing events loop stopped.");
              },
              cancellationToken);
    }

    private bool IsNextEventDue(TimeSpan avgProcessingTime, ref TimeSpan waitTime)
    {
        lock (this.eventsAvailableLock)
        {
            if (this.eventsQueue.FirstOrDefault() is EventsNode nextNode)
            {
                // The estimated time to wait here is comes from the difference between the next node's
                // target time to run and our projected completion time (which is the current time + the average processing time known).
                waitTime = nextNode.TargetTime - (CurrentTime + avgProcessingTime);
            }
        }

        return waitTime <= TimeSpan.Zero;
    }

    private EventsNode DequeueEventNode()
    {
        lock (this.eventsAvailableLock)
        {
            if (!this.eventsQueue.Any())
            {
                return null;
            }

            var node = this.eventsQueue.First.ValueRef;

            this.eventsQueue.RemoveFirst();

            return node;
        }
    }

    /// <summary>
    /// Enqueues an event into the event queue, appending it to an existing node or creating a new one for it.
    /// </summary>
    /// <param name="evt">The event to enqueue.</param>
    /// <param name="targetTime">The target time for the event, which gets rounded to fit <see cref="timeToRoundBy"/>.</param>
    private void EnqueueEvent(Event evt, ref DateTimeOffset targetTime)
    {
        targetTime = targetTime.Round(this.timeToRoundBy);

        lock (this.eventsAvailableLock)
        {
            evt.State = EventState.InQueue;
            evt.NextReadyTime = targetTime;

            this.eventsIndex[evt.Id] = evt;

            // If the queue is empty, we just add the first node.
            if (this.eventsQueue.Count == 0)
            {
                this.eventNodesIndex[evt.Id] = new EventsNode(targetTime, evt);

                this.eventsQueue.AddFirst(this.eventNodesIndex[evt.Id]);
                return;
            }

            // The queue is not empty, so we need to insert at the right spot.
            var currentNode = this.eventsQueue.First;

            while (targetTime > currentNode.ValueRef.TargetTime && currentNode.Next != null)
            {
                currentNode = currentNode.Next;
            }

            if (currentNode.ValueRef.TargetTime == targetTime)
            {
                this.eventNodesIndex[evt.Id] = currentNode.ValueRef;

                currentNode.ValueRef.Events.Add(evt);

                return;
            }

            var newNode = new EventsNode(targetTime, evt);

            this.eventNodesIndex[evt.Id] = newNode;

            if (targetTime < currentNode.ValueRef.TargetTime)
            {
                this.eventsQueue.AddBefore(currentNode, newNode);
            }
            else
            {
                this.eventsQueue.AddAfter(currentNode, newNode);
            }
        }
    }

    /// <summary>
    /// Attempts to cancel an event.
    /// </summary>
    /// <param name="evt">The event to cancel.</param>
    /// <returns>True if the event is cancelled, false otherwise.</returns>
    private bool Cancel(Event evt)
    {
        evt.ThrowIfNull(nameof(evt));

        // Lock on the events available to prevent race conditions on cancel vs firing.
        lock (this.eventsAvailableLock)
        {
            evt.State = EventState.Cancelled;
            evt.NextReadyTime = null;
        }

        this.Logger.LogTrace("Event {eventName} with id {eventId} was cancelled.", evt.GetType().Name, evt.Id);

        return true;
    }

    /// <summary>
    /// Attempts to delay this event, pushing its next ready time into the future.
    /// </summary>
    /// <param name="evt">The event to delay.</param>
    /// <param name="delayBy">The amount of time to delay the event by.</param>
    /// <returns>True if the event is successfully delayed, and false otherwise.</returns>
    private bool Delay(Event evt, TimeSpan delayBy)
    {
        evt.ThrowIfNull(nameof(evt));

        if (delayBy <= TimeSpan.Zero)
        {
            throw new InvalidOperationException($"Expected delay time to be a positive value, but got {delayBy}.");
        }

        // Lock on the events available to prevent race conditions on cancel vs firing.
        lock (this.eventsAvailableLock)
        {
            if (!this.eventNodesIndex.TryGetValue(evt.Id, out EventsNode node))
            {
                this.Logger.LogTrace("A node for event with id {eventId} was not found in the index.", evt.Id);

                return false;
            }

            if (!node.Events.Remove(evt))
            {
                this.Logger.LogTrace("Event {eventName} with id {eventId} could not be delayed.", evt.GetType().Name, evt.Id);

                return false;
            }

            var newTargetTime = (evt.NextReadyTime + delayBy).Value;

            this.EnqueueEvent(evt, ref newTargetTime);

            this.Logger.LogTrace("Event {eventName} with id {eventId} was delayed to {timeTicks}.", evt.GetType().Name, evt.Id, newTargetTime.Ticks);
        }

        return true;
    }

    private bool Hurry(Event evt, TimeSpan hurryBy)
    {
        evt.ThrowIfNull(nameof(evt));

        if (hurryBy <= TimeSpan.Zero)
        {
            throw new InvalidOperationException($"Expected hurry time to be a positive value, but got {hurryBy}.");
        }

        var isExpedite = hurryBy == TimeSpan.MaxValue;
        var action = isExpedite ? "expedited" : "hurried";

        // Lock on the events available to prevent race conditions on cancel vs firing.
        lock (this.eventsAvailableLock)
        {
            if (!this.eventNodesIndex.TryGetValue(evt.Id, out EventsNode node))
            {
                this.Logger.LogTrace("A node for event with id {eventId} was not found in the index.", evt.Id);

                return false;
            }

            if (!node.Events.Remove(evt))
            {
                this.Logger.LogTrace("Event {eventName} with id {eventId} could not be {action}.", evt.GetType().Name, evt.Id, action);

                return false;
            }

            var newTargetTime = isExpedite ? CurrentTime : (evt.NextReadyTime - hurryBy).Value;

            this.EnqueueEvent(evt, ref newTargetTime);

            this.Logger.LogTrace("Event {eventName} with id {eventId} was {action} to {timeTicks}.", evt.GetType().Name, evt.Id, action, newTargetTime.Ticks);

            // Pulse the monitor since we might have added at the beginning of the queue.
            Monitor.Pulse(this.eventsAvailableLock);
        }

        return true;
    }
}
