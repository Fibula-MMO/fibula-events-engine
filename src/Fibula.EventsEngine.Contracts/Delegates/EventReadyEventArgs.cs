// -----------------------------------------------------------------
// <copyright file="EventReadyEventArgs.cs" company="2Dudes">
// Copyright (c) | Jose L. Nunez de Caceres et al.
// https://linkedin.com/in/nunezdecaceres
//
// All Rights Reserved.
//
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------

namespace Fibula.EventsEngine.Contracts.Delegates;

using System;
using Fibula.EventsEngine.Contracts.Abstractions;
using Fibula.Utilities.Validation;

/// <summary>
/// Class that represents the event arguments of an <see cref="EventReadyDelegate"/> event.
/// </summary>
public class EventReadyEventArgs : EventArgs
{
    /// <summary>
    /// Initializes a new instance of the <see cref="EventReadyEventArgs"/> class.
    /// </summary>
    /// <param name="evt">The target event.</param>
    public EventReadyEventArgs(IEvent evt)
    {
        evt.ThrowIfNull(nameof(evt));

        this.Event = evt;
    }

    /// <summary>
    /// Gets the event that is ready.
    /// </summary>
    public IEvent Event { get; }
}
