// -----------------------------------------------------------------
// <copyright file="IEvent.cs" company="2Dudes">
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
using Fibula.EventsEngine.Contracts.Enumerations;

/// <summary>
/// Interface that represents an event.
/// </summary>
public interface IEvent
{
    /// <summary>
    /// Gets a unique identifier for this event.
    /// </summary>
    Guid Id { get; }

    /// <summary>
    /// Gets a value indicating whether the event can be cancelled.
    /// </summary>
    bool CanBeCancelled { get; }

    /// <summary>
    /// Gets a value indicating whether to exclude this event from telemetry logging.
    /// </summary>
    bool ExcludeFromTelemetry { get; }

    /// <summary>
    /// Gets or sets the event's state.
    /// </summary>
    EventState State { get; set; }

    /// <summary>
    /// Gets or sets the time at which the event is expected to be processed, if any.
    /// </summary>
    /// <remarks>
    /// The event is only expected to have a value here if it is in the
    /// <see cref="EventState.InQueue"/> or <see cref="EventState.Executed"/> states.
    /// </remarks>
    DateTimeOffset? NextExecutionTime { get; set; }

    /// <summary>
    /// Executes the event logic.
    /// </summary>
    /// <param name="context">The execution context.</param>
    void Execute(IEventExecutionContext context);
}
