// -----------------------------------------------------------------
// <copyright file="BaseEvent.cs" company="2Dudes">
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
using System.Diagnostics.CodeAnalysis;
using Fibula.EventsEngine.Contracts.Abstractions;
using Fibula.EventsEngine.Contracts.Enumerations;

/// <summary>
/// Abstract class that represents the base event for scheduling.
/// </summary>
public abstract class BaseEvent : IEvent, IEquatable<BaseEvent>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="BaseEvent"/> class.
    /// </summary>
    public BaseEvent()
    {
        this.Id = Guid.NewGuid();
        this.State = EventState.Created;
    }

    /// <summary>
    /// Gets a unique identifier for this event.
    /// </summary>
    public Guid Id { get; }

    /// <summary>
    /// Gets or sets a value indicating whether the event can be cancelled.
    /// </summary>
    public abstract bool CanBeCancelled { get; protected set; }

    /// <summary>
    /// Gets or sets a value indicating whether to exclude this event from telemetry logging.
    /// </summary>
    public bool ExcludeFromTelemetry { get; protected set; }

    /// <summary>
    /// Gets the event's state.
    /// </summary>
    public EventState State { get; internal set; }

    /// <summary>
    /// Gets the time at which the event is expected to be processed, if any.
    /// </summary>
    /// <remarks>
    /// The event is only expected to have a value here if it is in the
    /// <see cref="EventState.InQueue"/> or <see cref="EventState.Executed"/> states.
    /// </remarks>
    public DateTimeOffset? NextExecutionTime { get; internal set; }

    /// <summary>
    /// Executes the event logic.
    /// </summary>
    /// <param name="context">The execution context.</param>
    public abstract void Execute(IEventExecutionContext context);

    /// <summary>
    /// Indicates whether the current object is equal to another object of the same type.
    /// </summary>
    /// <param name="other">The other object to compare against.</param>
    /// <returns>True if the current object is equal to the other parameter, false otherwise.</returns>
    public bool Equals([AllowNull] BaseEvent other)
    {
        return this.Id == other?.Id;
    }

    /// <inheritdoc/>
    public override bool Equals(object obj)
    {
        return this.Equals(obj as BaseEvent);
    }

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        return this.Id.GetHashCode();
    }
}
