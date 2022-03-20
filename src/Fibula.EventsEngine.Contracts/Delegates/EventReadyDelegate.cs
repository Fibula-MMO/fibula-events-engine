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

using System;
using Fibula.EventsEngine.Contracts.Abstractions;

/// <summary>
/// Delegate to call when an event is ready to be executed.
/// </summary>
/// <param name="sender">The sender of the event.</param>
/// <param name="evt">The event that was marked ready.</param>
/// <param name="timeDrift">The time drift observed between the intended ready time and actual ready time.</param>
public delegate void EventReadyDelegate(object sender, IEvent evt, TimeSpan timeDrift);
