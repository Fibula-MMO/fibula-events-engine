// -----------------------------------------------------------------
// <copyright file="EventsReactorOptions.cs" company="2Dudes">
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
using System.ComponentModel.DataAnnotations;

/// <summary>
/// Class that represents options for the <see cref="EventsReactor"/>.
/// </summary>
public class EventsReactorOptions
{
    /// <summary>
    /// Gets or sets the milliseconds to round the events scheduled by.
    /// </summary>
    [Required(ErrorMessage = "An event round-by milliseconds must be specified.")]
    [Range(50, 1000, ErrorMessage = "The specified event round-by milliseconds must be between 50 and 1000.")]
    public int? EventRoundByMilliseconds { get; set; }
}
