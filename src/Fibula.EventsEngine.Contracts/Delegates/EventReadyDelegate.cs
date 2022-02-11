// -----------------------------------------------------------------
// <copyright file="EventReadyDelegate.cs" company="2Dudes">
// Copyright (c) | Jose L. Nunez de Caceres et al.
// https://linkedin.com/in/nunezdecaceres
//
// All Rights Reserved.
//
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------

namespace Fibula.EventsEngine.Contracts.Delegates;

/// <summary>
/// Delegate to call when an event is ready to be executed.
/// </summary>
/// <param name="sender">The sender of the event.</param>
/// <param name="eventArgs">The event arguments that contain the actual event.</param>
public delegate void EventReadyDelegate(object sender, EventReadyEventArgs eventArgs);
