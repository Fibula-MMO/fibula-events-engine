// -----------------------------------------------------------------
// <copyright file="EventsNode.cs" company="2Dudes">
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
using System.Linq;

/// <summary>
/// Class that represents a node of the events queue used in the reactor.
/// </summary>
internal sealed class EventsNode
{
    /// <summary>
    /// Initializes a new instance of the <see cref="EventsNode"/> class.
    /// </summary>
    /// <param name="targetTime">The target time for events in this node.</param>
    /// <param name="events">The events that compose this node.</param>
    public EventsNode(DateTimeOffset targetTime, params BaseEvent[] events)
    {
        if (events.Length == 0)
        {
            throw new ArgumentException($"At least one event must be specified.", nameof(events));
        }

        this.TargetTime = targetTime;
        this.Events = events.ToList();
    }

    /// <summary>
    /// Gets the target time for events in this node.
    /// </summary>
    public DateTimeOffset TargetTime { get; private set; }

    /// <summary>
    /// Gets the event that the node references.
    /// </summary>
    public IList<BaseEvent> Events { get; private set; }
}
