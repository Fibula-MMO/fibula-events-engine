// -----------------------------------------------------------------
// <copyright file="Event.cs" company="2Dudes">
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
using Fibula.EventsEngine.Contracts.Abstractions;
using Fibula.EventsEngine.Contracts.Enumerations;

/// <summary>
/// Abstract class that represents the base event for scheduling.
/// </summary>
public abstract class Event : IEvent, IEquatable<Event>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Event"/> class.
    /// </summary>
    public Event()
    {
        this.Id = Guid.NewGuid();
        this.State = EventState.Created;
    }

    /// <summary>
    /// Gets a unique identifier for this event.
    /// </summary>
    public Guid Id { get; }

    /// <summary>
    /// Gets the event's state.
    /// </summary>
    public EventState State { get; internal set; }

    /// <summary>
    /// Gets the time at which the event is expected to be processed, if any.
    /// </summary>
    /// <remarks>
    /// The event is only expected to have a value here if it is in the
    /// <see cref="EventState.InQueue"/> or <see cref="EventState.Executing"/> states.
    /// </remarks>
    public DateTimeOffset? NextReadyTime { get; internal set; }

    /// <summary>
    /// Indicates whether the current object is equal to another object of the same type.
    /// </summary>
    /// <param name="other">The other object to compare against.</param>
    /// <returns>True if the current object is equal to the other parameter, false otherwise.</returns>
    public bool Equals(Event other)
    {
        return this.Id == other?.Id;
    }

    /// <inheritdoc/>
    public override bool Equals(object obj)
    {
        return this.Equals(obj as Event);
    }

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        return this.Id.GetHashCode();
    }
}
