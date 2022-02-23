// -----------------------------------------------------------------
// <copyright file="EventsReactorTests.cs" company="2Dudes">
// Copyright (c) | Jose L. Nunez de Caceres et al.
// https://linkedin.com/in/nunezdecaceres
//
// All Rights Reserved.
//
// Licensed under the MIT License. See LICENSE in the project root for license information.
// </copyright>
// -----------------------------------------------------------------

namespace Fibula.EventsEngine.Tests;

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Fibula.EventsEngine.Contracts.Delegates;
using Fibula.Utilities.Common.Extensions;
using Fibula.Utilities.Testing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

/// <summary>
/// Tests for the <see cref="EventsReactor"/> class.
/// </summary>
[TestClass]
public class EventsReactorTests
{
    /// <summary>
    /// Default reactor options known to be good.
    /// </summary>
    private static readonly IOptions<EventsReactorOptions> GoodReactorOptions = Options.Create(new EventsReactorOptions() { EventRoundByMilliseconds = 100 });

    /// <summary>
    /// Checks <see cref="EventsReactor"/> initialization.
    /// </summary>
    [TestMethod]
    public void EventsReactor_Initialization()
    {
        var loggerMock = Mock.Of<ILogger<EventsReactor>>();
        IOptions<EventsReactorOptions> goodOptions = Options.Create(new EventsReactorOptions() { EventRoundByMilliseconds = 50 });
        IOptions<EventsReactorOptions> noEventRoundByOptions = Options.Create(new EventsReactorOptions());
        IOptions<EventsReactorOptions> roundTimeBelowRangeOptions = Options.Create(new EventsReactorOptions() { EventRoundByMilliseconds = 49 });
        IOptions<EventsReactorOptions> roundTimeAboveRangeOptions = Options.Create(new EventsReactorOptions() { EventRoundByMilliseconds = 1001 });

        // Supplying no logger instance should throw.
        ExceptionAssert.Throws<ArgumentNullException>(() => new EventsReactor(null, null), "Value cannot be null. (Parameter 'logger')");
        ExceptionAssert.Throws<ArgumentNullException>(() => new EventsReactor(loggerMock, null), "Value cannot be null. (Parameter 'reactorOptions')");

        ExceptionAssert.Throws<ValidationException>(() => new EventsReactor(loggerMock, noEventRoundByOptions), $"At least one validation error found for EventsReactorOptions:{Environment.NewLine}  1) An event round-by milliseconds must be specified");
        ExceptionAssert.Throws<ValidationException>(() => new EventsReactor(loggerMock, roundTimeBelowRangeOptions), $"At least one validation error found for EventsReactorOptions:{Environment.NewLine}  1) The specified event round-by milliseconds must be between 50 and 1000.");
        ExceptionAssert.Throws<ValidationException>(() => new EventsReactor(loggerMock, roundTimeAboveRangeOptions), $"At least one validation error found for EventsReactorOptions:{Environment.NewLine}  1) The specified event round-by milliseconds must be between 50 and 1000.");

        // use a non default reference time.
        new EventsReactor(loggerMock, goodOptions);
    }

    /// <summary>
    /// Checks that <see cref="EventsReactor.Cancel(Guid)"/> does what it should.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [TestMethod]
    public async Task Cancelling_SingleEvent()
    {
        const int ExpectedCounterValueBeforeRun = 0;
        const int ExpectedCounterValueAfterRun = 0;
        const int MinimumOverheadDelayMs = 200;

        TimeSpan overheadDelay = TimeSpan.FromMilliseconds(MinimumOverheadDelayMs);
        TimeSpan twoSecondsTimeSpan = TimeSpan.FromSeconds(2);
        TimeSpan threeSecondsTimeSpan = TimeSpan.FromSeconds(3);

        var eventFiredCounter = 0;
        var eventMock = new Mock<BaseEvent>();

        eventMock.SetupGet(e => e.CanBeCancelled).Returns(true);

        EventsReactor reactor = SetupReactorWithConsoleLogger(Options.Create(new EventsReactorOptions() { EventRoundByMilliseconds = MinimumOverheadDelayMs }));

        using var cts = new CancellationTokenSource();

        reactor.EventReady += (sender, eventArgs) =>
        {
            // test that sender is the same reactor instance, while we're here.
            Assert.AreEqual(reactor, sender);

            // check that event has a reference.
            Assert.IsNotNull(eventArgs?.Event);

            if (eventArgs.Event == eventMock.Object)
            {
                eventFiredCounter++;
            }
        };

        // start the reactor.
        Task reactorTask = reactor.RunAsync(cts.Token);

        // push an event that shall be ready after a couple of seconds.
        reactor.Push(eventMock.Object, twoSecondsTimeSpan);

        // delay for 100 ms (to account for setup overhead and multi threading) and check that the counter has NOT gone up,
        // since the event we pushed before should not be ready yet.
        await Task.Delay(overheadDelay).ContinueWith(prev =>
        {
            Assert.AreEqual(ExpectedCounterValueBeforeRun, eventFiredCounter, $"Events counter does not match: Expected {ExpectedCounterValueBeforeRun}, got {eventFiredCounter}.");
        });

        // cancel this event.
        reactor.Cancel(eventMock.Object.Id);

        // delay for three seconds and check that the counter has NOT gone up, since we cancelled the event.
        await Task.Delay(threeSecondsTimeSpan).ContinueWith(prev =>
        {
            Assert.AreEqual(ExpectedCounterValueAfterRun, eventFiredCounter, $"Events counter does not match: Expected {ExpectedCounterValueAfterRun}, got {eventFiredCounter}.");
        });
    }

    /// <summary>
    /// Checks that <see cref="EventsReactor.EventReady"/> gets fired when an event goes through the reactor and delay.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [TestMethod]
    public async Task OnEventReady_IsCalled()
    {
        const int ExpectedCounterValueBeforeRun = 0;
        const int ExpectedCounterValueAfterRun = 1;

        TimeSpan twoSecondsTimeSpan = TimeSpan.FromSeconds(2);
        TimeSpan overheadDelay = TimeSpan.FromMilliseconds(100);

        BaseEvent eventWithNoDelay = Mock.Of<BaseEvent>();
        BaseEvent eventWithDelay = Mock.Of<BaseEvent>();

        var reactor = SetupReactorWithConsoleLogger();

        var inmediateEventFiredCounter = 0;
        var delayedEventFiredCounter = 0;

        using var cts = new CancellationTokenSource();

        reactor.EventReady += (sender, eventArgs) =>
        {
            // test that sender is the same reactor instance, while we're here.
            Assert.AreEqual(reactor, sender);

            // check that event has a reference.
            Assert.IsNotNull(eventArgs?.Event);

            if (eventArgs.Event == eventWithNoDelay)
            {
                inmediateEventFiredCounter++;
            }
            else if (eventArgs.Event == eventWithDelay)
            {
                delayedEventFiredCounter++;
            }
        };

        // start the reactor.
        Task reactorTask = reactor.RunAsync(cts.Token);

        await Task.Delay(overheadDelay).ContinueWith(prev =>
        {
            // push an event with no delay.
            reactor.Push(eventWithNoDelay);

            // push an event with a delay of some seconds.
            reactor.Push(eventWithDelay, twoSecondsTimeSpan);
        }).ContinueWith(prev =>
        {
            Assert.AreEqual(ExpectedCounterValueBeforeRun, delayedEventFiredCounter, $"Delayed events counter does not match: Expected {ExpectedCounterValueBeforeRun}, got {delayedEventFiredCounter}.");
        });

        // delay for 500 ms and check that the counter has gone up.
        await Task.Delay(overheadDelay).ContinueWith(prev =>
        {
            Assert.AreEqual(ExpectedCounterValueAfterRun, inmediateEventFiredCounter, $"Inmediate events counter does not match: Expected {ExpectedCounterValueAfterRun}, got {inmediateEventFiredCounter}.");
        });

        // delay for the remaining seconds and check that the counter has gone up for delayed events.
        await Task.Delay(twoSecondsTimeSpan).ContinueWith(prev =>
        {
            Assert.AreEqual(ExpectedCounterValueAfterRun, delayedEventFiredCounter, $"Delayed events counter does not match: Expected {ExpectedCounterValueAfterRun}, got {delayedEventFiredCounter}.");
        });
    }

    /// <summary>
    /// Tests that the events reactor works as intended under simulated load.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [TestMethod]
    public async Task TestRandomizedLoad()
    {
        // We simulate a load of 1000 events out of which about 20% get cancelled and 80% don't.
        // The events will have randomized delays, some of which might fall outside of the test's TTL.
        // The final set of processed events must match the expected value for events that fall within the test's TTL.
        const int LoadSize = 10000;
        const int RoundByMs = 500;
        var cutoffTime = TimeSpan.FromMilliseconds(5000);

        // The time drift should be around half of the RoundBy milliseconds, but we give an extra 10% cushion since
        // there ought to be some processing time error.
        var highestAcceptableTimeDiff = TimeSpan.FromMilliseconds(RoundByMs * 0.6);
        var eventsDictionary = new Dictionary<Guid, TimeSpan>();
        var rng = new Random(1337);

        var reactor = SetupReactorWithConsoleLogger(Options.Create(new EventsReactorOptions() { EventRoundByMilliseconds = RoundByMs }));

        void OnEventReadyFunc(object sender, EventReadyEventArgs eventArgs)
        {
            // test that sender is the same reactor instance, while we're here.
            Assert.AreEqual(reactor, sender);

            // check that event has a reference.
            Assert.IsNotNull(eventArgs?.Event);

            if (!eventsDictionary.ContainsKey(eventArgs.Event.Id))
            {
                throw new InvalidOperationException($"Unknown event with id {eventArgs.Event.Id} received.");
            }

            eventsDictionary[eventArgs.Event.Id] = eventArgs.TimeDrift;
        }

        reactor.EventReady += OnEventReadyFunc;

        using var cts = new CancellationTokenSource();

        // start the reactor.
        Task reactorTask = reactor.RunAsync(cts.Token);

        for (int i = 0; i < LoadSize; i++)
        {
            BaseEvent evt = Mock.Of<BaseEvent>();

            // let's do a 50% chance to delay this event.
            var delayThisEvent = rng.Next(2) == 0;

            eventsDictionary[evt.Id] = TimeSpan.Zero;

            reactor.Push(evt, delayThisEvent ? TimeSpan.FromSeconds(rng.Next(10)) : null);
        }

        await Task.Delay(cutoffTime).ContinueWith(prev =>
        {
            // unsubscribe to prevent more events polluting the results.
            reactor.EventReady -= OnEventReadyFunc;

            foreach (var (evtId, timeDrift) in eventsDictionary)
            {
                var isAcceptableTimeDrift = timeDrift >= TimeSpan.Zero && timeDrift <= highestAcceptableTimeDiff;

                Assert.IsTrue(isAcceptableTimeDrift, $"Event {evtId} time difference was too large: {timeDrift}.");
            }
        });
    }

    /// <summary>
    /// Helper method used to setup a <see cref="EventsReactor"/> instance with a console logger.
    /// </summary>
    /// <returns>The reactor instance.</returns>
    private static EventsReactor SetupReactorWithConsoleLogger(IOptions<EventsReactorOptions> options = null)
    {
        using var logFactory = LoggerFactory.Create(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Trace));

        var logger = logFactory.CreateLogger<EventsReactor>();

        return new EventsReactor(logger, options ?? GoodReactorOptions);
    }
}
