// -----------------------------------------------------------------
// <copyright file="EventState.cs" company="2Dudes">
// Copyright (c) | Jose L. Nunez de Caceres et al.
// https://linkedin.com/in/nunezdecaceres
//
// All Rights Reserved.
//
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------

namespace Fibula.EventsEngine.Contracts.Enumerations
{
    /// <summary>
    /// Enumerates all the possible states for an event.
    /// </summary>
    public enum EventState
    {
        /// <summary>
        /// The starting state of events.
        /// </summary>
        Created,

        /// <summary>
        /// The event has been enqueued and is waiting to be executed.
        /// </summary>
        InQueue,

        /// <summary>
        /// The event has been cancelled.
        /// </summary>
        Cancelled,

        /// <summary>
        /// The event is being executed.
        /// </summary>
        Executing,

        /// <summary>
        /// The event has been executed.
        /// </summary>
        Executed,
    }
}
