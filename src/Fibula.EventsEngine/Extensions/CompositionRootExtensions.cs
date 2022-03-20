// -----------------------------------------------------------------
// <copyright file="CompositionRootExtensions.cs" company="2Dudes">
// Copyright (c) | Jose L. Nunez de Caceres et al.
// https://linkedin.com/in/nunezdecaceres
//
// All Rights Reserved.
//
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------

namespace Fibula.EventsEngine.Extensions;

using Fibula.EventsEngine;
using Fibula.EventsEngine.Contracts.Abstractions;
using Microsoft.Extensions.DependencyInjection;

/// <summary>
/// Static class that adds convenient methods to add the concrete implementations contained in this library.
/// </summary>
public static class CompositionRootExtensions
{
    /// <summary>
    /// Adds all implementations related to the events reactor contained in this library to the services collection.
    /// </summary>
    /// <param name="services">The services collection.</param>
    public static void AddEventsReactor(this IServiceCollection services)
    {
        services.AddSingleton<IEventsReactor<Event>, EventsReactor>();
    }
}
