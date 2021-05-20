// -----------------------------------------------------------------
// <copyright file="SchedulerOptions.cs" company="2Dudes">
// Copyright (c) | Jose L. Nunez de Caceres et al.
// https://linkedin.com/in/nunezdecaceres
//
// All Rights Reserved.
//
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------

namespace Fibula.Scheduling
{
    using System;
    using System.ComponentModel.DataAnnotations;

    /// <summary>
    /// Class that represents options for the <see cref="Scheduler"/>.
    /// </summary>
    public class SchedulerOptions
    {
        /// <summary>
        /// Gets or sets the milliseconds to round the events scheduled by.
        /// </summary>
        [Required(ErrorMessage = "An event round-by milliseconds must be specified.")]
        [Range(50, 1000, ErrorMessage = "The specified event round-by milliseconds must be between 50 and 1000.")]
        public int? EventRoundByMilliseconds { get; set; }

        /// <summary>
        /// Gets or sets the maximum time for the scheduler to wait before checking for more events.
        /// </summary>
        [Range(typeof(TimeSpan), "00:00:05", "00:30:00", ErrorMessage = "The specified maximum time to wait for events must be between 5 seconds and 30 minutes.")]
        public TimeSpan? MaximumWaitTime { get; set; }

        /// <summary>
        /// Gets or sets the starting queue size.
        /// </summary>
        [Range(64, int.MaxValue, ErrorMessage = "The starting queue size must be between 64 and 2147483647.")]
        public int? StartingQueueSize { get; set; }
    }
}
