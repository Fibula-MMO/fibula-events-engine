// -----------------------------------------------------------------
// <copyright file="EventExecutionContext.cs" company="2Dudes">
// Copyright (c) | Jose L. Nunez de Caceres et al.
// https://linkedin.com/in/nunezdecaceres
//
// All Rights Reserved.
//
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------

namespace Fibula.EventsEngine;

using Fibula.EventsEngine.Contracts.Abstractions;
using Fibula.Utilities.Validation;
using Microsoft.Extensions.Logging;

/// <summary>
/// Class that represents an event context.
/// </summary>
public class EventExecutionContext : IEventExecutionContext
{
    /// <summary>
    /// Initializes a new instance of the <see cref="EventExecutionContext"/> class.
    /// </summary>
    /// <param name="logger">A reference to the logger in use.</param>
    public EventExecutionContext(ILogger logger)
    {
        logger.ThrowIfNull(nameof(logger));

        this.Logger = logger;
    }

    /// <summary>
    /// Gets a reference to the logger in use.
    /// </summary>
    public ILogger Logger { get; }
}
