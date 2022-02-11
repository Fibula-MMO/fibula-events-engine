﻿// -----------------------------------------------------------------
// <copyright file="IEventExecutionContext.cs" company="2Dudes">
// Copyright (c) | Jose L. Nunez de Caceres et al.
// https://linkedin.com/in/nunezdecaceres
//
// All Rights Reserved.
//
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------

namespace Fibula.EventsEngine.Contracts.Abstractions;

using Microsoft.Extensions.Logging;

/// <summary>
/// Interface for the execution context in events.
/// </summary>
public interface IEventExecutionContext
{
    /// <summary>
    /// Gets a reference to the logger in use.
    /// </summary>
    ILogger Logger { get; }
}
