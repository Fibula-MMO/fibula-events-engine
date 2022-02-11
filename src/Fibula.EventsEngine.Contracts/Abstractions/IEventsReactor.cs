// -----------------------------------------------------------------
// <copyright file="IEventsReactor.cs" company="2Dudes">
// Copyright (c) | Jose L. Nunez de Caceres et al.
// https://linkedin.com/in/nunezdecaceres
//
// All Rights Reserved.
//
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------

namespace Fibula.EventsEngine.Contracts.Abstractions;

using System;
using System.Threading;
using System.Threading.Tasks;
using Fibula.EventsEngine.Contracts.Delegates;

/// <summary>
/// Interface that represents a reactor (pattern) for events.
/// </summary>
public interface IEventsReactor
{
    /// <summary>
    /// Event fired when the reactor has an event ready to execute.
    /// </summary>
    event EventReadyDelegate EventReady;

    /// <summary>
    /// Gets the number of events in the queue.
    /// </summary>
    int QueueSize { get; }

    /// <summary>
    /// Pushes an event into the reactor, asynchronously.
    /// </summary>
    /// <param name="eventToPush">The event to push.</param>
    /// <param name="delayBy">Optional. A delay after which the event should be fired. If left null, the event is scheduled to be fired ASAP.</param>
    void Push(IEvent eventToPush, TimeSpan? delayBy = null);

    /// <summary>
    /// Attempts to cancel an event.
    /// </summary>
    /// <param name="evtId">The id of the event to cancel.</param>
    /// <returns>True if the event was successfully cancelled, and false otherwise.</returns>
    bool Cancel(Guid evtId);

    /// <summary>
    /// Starts the reactor's event processing loop.
    /// </summary>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    /// <returns>A <see cref="Task"/> representing the asynchronous processing operation.</returns>
    Task RunAsync(CancellationToken cancellationToken);
}
