// -----------------------------------------------------------------
// <copyright file="IEventContext.cs" company="2Dudes">
// Copyright (c) | Jose L. Nunez de Caceres et al.
// https://linkedin.com/in/nunezdecaceres
//
// All Rights Reserved.
//
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------

namespace Fibula.Scheduling.Contracts.Abstractions
{
    using System;
    using Serilog;

    /// <summary>
    /// Interface for an operation context.
    /// </summary>
    public interface IEventContext
    {
        /// <summary>
        /// Gets a reference to the logger in use.
        /// </summary>
        ILogger Logger { get; }

        /// <summary>
        /// Gets the current time.
        /// </summary>
        DateTimeOffset CurrentTime { get; }
    }
}
