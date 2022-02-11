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
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Fibula.EventsEngine.Contracts.Abstractions;
using Fibula.EventsEngine.Contracts.Delegates;
using Fibula.EventsEngine.Contracts.Enumerations;
using Fibula.Utilities.Validation;
using Microsoft.Extensions.Logging;

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
    private readonly LinkedList<IEvent> eventsQueue;

    /// <summary>
    /// Internal queue to handle asynchronous event demultiplexing.
    /// </summary>
    private readonly ConcurrentQueue<(IEvent evt, DateTimeOffset requestTime, TimeSpan requestedDelay)> eventDemultiplexingQueue;

    /// <summary>
    /// A mapping of events to their Ids for O(1) lookup.
    /// </summary>
    private readonly Dictionary<Guid, IEvent> eventsIndex;

    /// <summary>
    /// Tracks the median node in the events queue.
    /// </summary>
    private LinkedListNode<IEvent> medianNode;

    /// <summary>
    /// Tracks the balance in the median, which is a measure of how many nodes
    /// there is to the sides of the median.
    /// </summary>
    private int medianBalance;

    /// <summary>
    /// Initializes a new instance of the <see cref="EventsReactor"/> class.
    /// </summary>
    /// <param name="logger">The logger to use.</param>
    public EventsReactor(ILogger<EventsReactor> logger)
    {
        logger.ThrowIfNull(nameof(logger));

        this.Logger = logger;

        this.eventsAvailableLock = new object();
        this.eventsPushedLock = new object();

        this.eventsQueue = new LinkedList<IEvent>();
        this.eventDemultiplexingQueue = new ConcurrentQueue<(IEvent evt, DateTimeOffset requestTime, TimeSpan requestedDelay)>();
        this.eventsIndex = new Dictionary<Guid, IEvent>();
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
        if (!this.eventsIndex.TryGetValue(evtId, out IEvent evt))
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

        if (delayBy == null || delayBy < TimeSpan.Zero)
        {
            delayBy = TimeSpan.Zero;
        }

        lock (this.eventsPushedLock)
        {
            this.eventDemultiplexingQueue.Enqueue((eventToPush, CurrentTime, delayBy.Value));

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
                        // Max timeout accepted in Monitor.Wait is 2^31 - 1 milliseconds.
                        waitForNewTimeOut = maxTimeout;
                        sw.Restart();

                        if (this.eventsQueue.First == null)
                        {
                            // no more items on the queue, go to wait.
                            this.Logger.LogWarning("The events queue is empty.");
                            continue;
                        }

                        // Maintain the average processing time to adjust the decision whether the next event is
                        // within the execution time epsilon.
                        var avgProcessingTime = cyclesProcessed == 0 ? 0 : cycleTimeTotal / cyclesProcessed++;

                        // Check the current queue and fire any events that are due.
                        while (this.eventsQueue.Any())
                        {
                            // The first item always points to the next-in-time event available.
                            var nextEvent = this.eventsQueue.First.ValueRef;
                            var projectedCompletionTime = CurrentTime + TimeSpan.FromMilliseconds(avgProcessingTime);
                            var timeDifference = nextEvent.NextExecutionTime.Value - projectedCompletionTime;
                            var isDue = timeDifference <= TimeSpan.Zero;
                            var wasCancelled = nextEvent.State == EventState.Cancelled;

                            // Check if this event has been cancelled or is due.
                            // If so, we need to dump it, or process it, respectively.
                            if (isDue || wasCancelled)
                            {
                                // Actually dequeue the event.
                                this.DequeueEvent();

                                if (!wasCancelled)
                                {
                                    this.Logger.LogTrace($"Event {nextEvent.GetType().Name} with id {nextEvent.Id}, marked ready at {CurrentTime}.");

                                    nextEvent.State = EventState.Executing;

                                    this.EventReady?.Invoke(this, new EventReadyEventArgs(nextEvent));

                                    nextEvent.State = EventState.Executed;
                                }
                                else
                                {
                                    this.Logger.LogTrace($"Event {nextEvent.GetType().Name} with id {nextEvent.Id} ignored, it was cancelled.");
                                }

                                // Clean up the event.
                                this.eventsIndex.Remove(nextEvent.Id);
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
                              while (this.eventDemultiplexingQueue.TryDequeue(out (IEvent Event, DateTimeOffset RequestedAt, TimeSpan Delay) request))
                              {
                                  var currentTime = CurrentTime;
                                  var targetTime = currentTime + request.Delay;
                                  var elapsedTime = currentTime - request.RequestedAt;
                                  var adjustedDelay = request.Delay - elapsedTime;

                                  this.EnqueueEvent(request.Event, currentTime + adjustedDelay);

                                  this.Logger.LogTrace($"Enqueued {request.Event.GetType().Name} with id {request.Event.Id}, due in {request.Delay.TotalMilliseconds} milliseconds (at {targetTime}).");
                              }

                              // Let the other loop know there's more work available.
                              Monitor.Pulse(this.eventsAvailableLock);
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

    private void EnqueueEvent(IEvent evt, DateTimeOffset targetTime)
    {
        lock (this.eventsAvailableLock)
        {
            if (this.eventsQueue.Count == 0)
            {
                this.eventsQueue.AddFirst(evt);
                this.medianNode = this.eventsQueue.First;
            }
            else if (targetTime <= this.medianNode.ValueRef.NextExecutionTime)
            {
                // Enqueue from the first and find the right spot.
                var currentNode = this.eventsQueue.First;

                while (currentNode.ValueRef.NextExecutionTime < targetTime
                    && currentNode.Next != null
                    && targetTime <= currentNode.Next.ValueRef.NextExecutionTime)
                {
                    currentNode = currentNode.Next;
                }

                if (currentNode.ValueRef.NextExecutionTime <= targetTime)
                {
                    this.eventsQueue.AddAfter(currentNode, evt);
                    this.medianBalance++;
                }
                else
                {
                    this.eventsQueue.AddBefore(currentNode, evt);
                    this.medianBalance--;
                }
            }
            else
            {
                // Enqueue from the last and find the right spot.
                var currentNode = this.eventsQueue.Last;

                while (targetTime < currentNode.ValueRef.NextExecutionTime
                    && currentNode.Previous != null
                    && currentNode.Previous.ValueRef.NextExecutionTime < targetTime)
                {
                    currentNode = currentNode.Previous;
                }

                if (currentNode.ValueRef.NextExecutionTime <= targetTime)
                {
                    this.eventsQueue.AddAfter(currentNode, evt);
                    this.medianBalance++;
                }
                else
                {
                    this.eventsQueue.AddBefore(currentNode, evt);
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
    private bool Cancel(IEvent evt)
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
        }

        this.Logger.LogTrace($"Event {evt.GetType().Name} with id {evt.Id} was cancelled.");

        return true;
    }
}
