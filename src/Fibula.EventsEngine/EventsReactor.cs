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
public class EventsReactor : IEventsReactor
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
    private readonly Queue<(BaseEvent evt, DateTimeOffset requestTime, TimeSpan requestedDelay)> eventDemultiplexingQueue;

    /// <summary>
    /// A mapping of events to their Ids for O(1) lookup.
    /// </summary>
    private readonly Dictionary<Guid, BaseEvent> eventsIndex;

    /// <summary>
    /// The time to round events target time by.
    /// </summary>
    private readonly TimeSpan timeToRoundBy;

    /// <summary>
    /// Tracks the median node in the events queue.
    /// </summary>
    private LinkedListNode<EventsNode> medianNode;

    /// <summary>
    /// Tracks the balance in the median, which is a measure of how many nodes
    /// there is to the sides of the median.
    /// </summary>
    private int medianBalance;

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
        this.eventDemultiplexingQueue = new Queue<(BaseEvent evt, DateTimeOffset requestTime, TimeSpan requestedDelay)>();
        this.eventsIndex = new Dictionary<Guid, BaseEvent>();
        this.timeToRoundBy = TimeSpan.FromMilliseconds(reactorOptions.Value.EventRoundByMilliseconds.Value);
        this.medianNode = null;
        this.medianBalance = 0;
    }

    /// <summary>
    /// Event fired when the reactor has an event ready to execute.
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
        if (!this.eventsIndex.TryGetValue(evtId, out BaseEvent evt))
        {
            this.Logger.LogTrace($"An event with id {evtId} was not found in the index.");

            return false;
        }

        return this.Cancel(evt);
    }

    /// <summary>
    /// Pushes an event into the reactor, asynchronously.
    /// </summary>
    /// <param name="eventToPush">The event to push.</param>
    /// <param name="delayBy">Optional. A delay after which the event should be fired. If left null, the event is scheduled to be fired ASAP.</param>
    public void Push(IEvent eventToPush, TimeSpan? delayBy = null)
    {
        eventToPush.ThrowIfNull(nameof(eventToPush));

        if (!(eventToPush is BaseEvent baseEvent))
        {
            throw new ArgumentException($"This reactor can only accept events derived from {nameof(BaseEvent)}");
        }

        if (delayBy == null || delayBy < TimeSpan.Zero)
        {
            delayBy = TimeSpan.Zero;
        }

        lock (this.eventsPushedLock)
        {
            this.eventDemultiplexingQueue.Enqueue((baseEvent, CurrentTime, delayBy.Value));

            this.Logger.LogTrace($"Pushed event {eventToPush.GetType().Name} with id {eventToPush.Id} for demultiplexing.");

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
                var waitForNewTimeOut = TimeSpan.Zero;
                var sw = new Stopwatch();

                long cyclesProcessed = 0;
                long cycleTimeTotal = 0;

                while (!cancellationToken.IsCancellationRequested)
                {
                    lock (this.eventsAvailableLock)
                    {
                        // Normalize to zero because the Monitor.Wait() call throws on negative values.
                        if (waitForNewTimeOut < TimeSpan.Zero)
                        {
                            waitForNewTimeOut = TimeSpan.Zero;
                        }

                        // Wait until we're flagged that there are events available.
                        Monitor.Wait(this.eventsAvailableLock, waitForNewTimeOut);

                        // Then, we reset time to wait and start the stopwatch.
                        waitForNewTimeOut = maxTimeout;
                        sw.Restart();

                        if (this.eventsQueue.First == null)
                        {
                            // no more items on the queue, go to wait.
                            this.Logger.LogWarning("The event processing loop was woken up but the events queue was empty.");
                            continue;
                        }

                        // Maintain the average processing time to adjust the decision whether the next event is
                        // within the execution time epsilon.
                        var avgProcessingTime = cyclesProcessed == 0 ? 0 : cycleTimeTotal / cyclesProcessed++;

                        // Check the current queue and fire any events that are due.
                        while (this.eventsQueue.Any())
                        {
                            // The first item always points to the next-in-time event available.
                            var nextEvents = this.eventsQueue.First.ValueRef;
                            var projectedCompletionTime = CurrentTime + TimeSpan.FromMilliseconds(avgProcessingTime);
                            var timeDifference = nextEvents.TargetTime - projectedCompletionTime;
                            var isDue = timeDifference <= TimeSpan.Zero;

                            // Check if these events node is due.
                            if (isDue)
                            {
                                // Actually dequeue the event.
                                this.DequeueEvent();

                                foreach (var baseEvt in nextEvents.Events)
                                {
                                    if (baseEvt.State != EventState.Cancelled)
                                    {
                                        this.Logger.LogTrace($"Event {nextEvents.GetType().Name} with id {baseEvt.Id}, marked ready at {CurrentTime}.");

                                        baseEvt.State = EventState.Executing;

                                        this.EventReady?.Invoke(this, new EventReadyEventArgs(baseEvt));

                                        baseEvt.State = EventState.Executed;
                                        baseEvt.NextExecutionTime = null;
                                    }
                                    else
                                    {
                                        this.Logger.LogTrace($"Event {nextEvents.GetType().Name} with id {baseEvt.Id} ignored, it was cancelled.");
                                    }

                                    // Clean up the event.
                                    this.eventsIndex.Remove(baseEvt.Id);
                                }
                            }
                            else
                            {
                                // The next item is in the future, so figure out how long to wait, update and break.
                                waitForNewTimeOut = timeDifference;
                                break;
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
                          Monitor.Wait(this.eventsPushedLock);

                          lock (this.eventsAvailableLock)
                          {
                              // Since we have locked on the events lock, we know that the processing loop is halted.
                              // Let's add the events waiting to be added to the queue as fast as possible.
                              while (this.eventDemultiplexingQueue.TryDequeue(out (BaseEvent Event, DateTimeOffset RequestedAt, TimeSpan Delay) request))
                              {
                                  var currentTime = CurrentTime;
                                  var targetTime = currentTime + request.Delay;
                                  var elapsedTime = currentTime - request.RequestedAt;
                                  var adjustedDelay = request.Delay - elapsedTime;
                                  var normalizedTargetTime = (currentTime + adjustedDelay).Round(this.timeToRoundBy);

                                  this.EnqueueEvent(request.Event, normalizedTargetTime);

                                  this.Logger.LogTrace($"Enqueued {request.Event.GetType().Name} with id {request.Event.Id}, due in {request.Delay.TotalMilliseconds} milliseconds (at {targetTime}).");
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

    private void DequeueEvent()
    {
        lock (this.eventsAvailableLock)
        {
            // we always dequeue the first element, so this only gets pushed into the positives.
            if (++this.medianBalance == 2)
            {
                this.medianNode = this.medianNode.Next;
                this.medianBalance = 0;
            }

            this.eventsQueue.RemoveFirst();

            if (this.eventsQueue.Count == 0)
            {
                this.medianNode = null;
                this.medianBalance = 0;
            }
        }
    }

    private void EnqueueEvent(BaseEvent evt, DateTimeOffset targetTime)
    {
        lock (this.eventsAvailableLock)
        {
            if (this.eventsQueue.Count == 0)
            {
                this.eventsQueue.AddFirst(new EventsNode(targetTime, evt));
                this.medianNode = this.eventsQueue.First;
            }
            else if (targetTime <= this.medianNode.ValueRef.TargetTime)
            {
                // Enqueue from the first and find the right spot.
                var currentNode = this.eventsQueue.First;

                while (currentNode.ValueRef.TargetTime < targetTime
                    && currentNode.Next != null
                    && targetTime <= currentNode.Next.ValueRef.TargetTime)
                {
                    currentNode = currentNode.Next;
                }

                if (currentNode.ValueRef.TargetTime == targetTime)
                {
                    currentNode.ValueRef.Events.Add(evt);
                }
                else if (currentNode.ValueRef.TargetTime < targetTime)
                {
                    this.eventsQueue.AddAfter(currentNode, new EventsNode(targetTime, evt));
                    this.medianBalance++;
                }
                else
                {
                    this.eventsQueue.AddBefore(currentNode, new EventsNode(targetTime, evt));
                    this.medianBalance--;
                }
            }
            else
            {
                // Enqueue from the last and find the right spot.
                var currentNode = this.eventsQueue.Last;

                while (targetTime < currentNode.ValueRef.TargetTime
                    && currentNode.Previous != null
                    && currentNode.Previous.ValueRef.TargetTime < targetTime)
                {
                    currentNode = currentNode.Previous;
                }

                if (currentNode.ValueRef.TargetTime == targetTime)
                {
                    currentNode.ValueRef.Events.Add(evt);
                }
                else if (currentNode.ValueRef.TargetTime < targetTime)
                {
                    this.eventsQueue.AddAfter(currentNode, new EventsNode(targetTime, evt));
                    this.medianBalance++;
                }
                else
                {
                    this.eventsQueue.AddBefore(currentNode, new EventsNode(targetTime, evt));
                    this.medianBalance--;
                }
            }

            if (this.medianBalance == -2)
            {
                this.medianNode = this.medianNode.Previous;
                this.medianBalance = 0;
            }

            if (this.medianBalance == 2)
            {
                this.medianNode = this.medianNode.Next;
                this.medianBalance = 0;
            }

            evt.State = EventState.InQueue;
            evt.NextExecutionTime = targetTime;

            this.eventsIndex.Add(evt.Id, evt);
        }
    }

    /// <summary>
    /// Attempts to cancel an event.
    /// </summary>
    /// <param name="evt">The event to cancel.</param>
    /// <returns>True if the event is cancelled, false otherwise.</returns>
    private bool Cancel(BaseEvent evt)
    {
        evt.ThrowIfNull();

        if (!evt.CanBeCancelled)
        {
            this.Logger.LogTrace($"Event {evt.GetType().Name} with id {evt.Id} cannot be cancelled.");

            return false;
        }

        // Lock on the events available to prevent race conditions on cancel vs firing.
        lock (this.eventsAvailableLock)
        {
            evt.State = EventState.Cancelled;
            evt.NextExecutionTime = null;
        }

        this.Logger.LogTrace($"Event {evt.GetType().Name} with id {evt.Id} was cancelled.");

        return true;
    }
}
