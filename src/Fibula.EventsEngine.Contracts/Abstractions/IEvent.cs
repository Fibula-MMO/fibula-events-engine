﻿// -----------------------------------------------------------------
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
    /// Gets the event's state.
    /// </summary>
    EventState State { get; }

    /// <summary>
    /// Gets the next time at which the event is expected to be marked ready to process, if any.
    /// </summary>
    /// <remarks>
    /// The event is only expected to have a value here if it is in the
    /// <see cref="EventState.InQueue"/> or <see cref="EventState.Executed"/> states.
    /// </remarks>
    DateTimeOffset? NextReadyTime { get; }
}
