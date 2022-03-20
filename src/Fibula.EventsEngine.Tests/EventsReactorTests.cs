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

#pragma warning disable CA1806 // Do not ignore method results

namespace Fibula.EventsEngine.Tests;

using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Threading;
using System.Threading.Tasks;
using Fibula.EventsEngine.Contracts.Abstractions;
using Fibula.Utilities.Testing;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Console;
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
        var eventMock = new Mock<Event>();

        EventsReactor reactor = SetupReactorWithConsoleLogger(Options.Create(new EventsReactorOptions() { EventRoundByMilliseconds = MinimumOverheadDelayMs }));

        using var cts = new CancellationTokenSource();

        reactor.EventReady += (sender, evt, timeDrift) =>
        {
            // test that sender is the same reactor instance, while we're here.
            Assert.AreEqual(reactor, sender);

            // check that event has a reference.
            Assert.IsNotNull(evt);

            if (evt == eventMock.Object)
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
    /// Checks that <see cref="EventsReactor.Delay(Guid, TimeSpan)"/> does what it should.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [TestMethod]
    public async Task Delaying_SingleEvent()
    {
        const int ExpectedCounterValueBeforeRun = 0;
        const int ExpectedCounterValueAfterDelay = 0;
        const int ExpectedCounterValueAfterRun = 1;

        TimeSpan twoSecondsTimeSpan = TimeSpan.FromSeconds(2);
        TimeSpan threeSecondsTimeSpan = TimeSpan.FromSeconds(3);

        var eventFiredCounter = 0;
        var eventMock = new Mock<Event>();

        EventsReactor reactor = SetupReactorWithConsoleLogger(Options.Create(new EventsReactorOptions() { EventRoundByMilliseconds = 200 }));

        using var cts = new CancellationTokenSource();

        reactor.EventReady += (sender, evt, timeDrift) =>
        {
            // test that sender is the same reactor instance, while we're here.
            Assert.AreEqual(reactor, sender);

            // check that event has a reference.
            Assert.IsNotNull(evt);

            if (evt == eventMock.Object)
            {
                eventFiredCounter++;
            }
        };

        // start the reactor.
        var reactorTask = reactor.RunAsync(cts.Token);

        // start testing.
        var testingTask = Task.Run(() =>
           {
               // push an event that shall be ready after some time...
               reactor.Push(eventMock.Object, twoSecondsTimeSpan);

               // Check that nothing has been processed yet.
               Assert.AreEqual(ExpectedCounterValueBeforeRun, eventFiredCounter, $"Events counter does not match: Expected {ExpectedCounterValueBeforeRun}, got {eventFiredCounter}.");

               // Check that we can delay the event.
               Assert.IsTrue(reactor.Delay(eventMock.Object.Id, threeSecondsTimeSpan));

               // Then wait two seconds and check that the counter has NOT gone up, since we delayed the event.
               Task.Delay(twoSecondsTimeSpan).Wait();

               Assert.AreEqual(ExpectedCounterValueAfterDelay, eventFiredCounter, $"Events counter does not match: Expected {ExpectedCounterValueAfterDelay}, got {eventFiredCounter}.");

               // wait three more seconds and check that the counter has gone up, since the event should have ran already.
               Task.Delay(threeSecondsTimeSpan).Wait();

               Assert.AreEqual(ExpectedCounterValueAfterRun, eventFiredCounter, $"Events counter does not match: Expected {ExpectedCounterValueAfterRun}, got {eventFiredCounter}.");
           });

        await testingTask.ContinueWith(p => cts.Cancel());
    }

    /// <summary>
    /// Checks that <see cref="EventsReactor.Hurry(Guid, TimeSpan)"/> does what it should.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [TestMethod]
    public async Task Hurrying_SingleEvent()
    {
        const int ExpectedCounterValueBeforeRun = 0;
        const int ExpectedCounterValueAfterHurry = 1;

        TimeSpan fiveSecondsTimeSpan = TimeSpan.FromSeconds(5);
        TimeSpan threeSecondsTimeSpan = TimeSpan.FromSeconds(3);

        var eventFiredCounter = 0;
        var eventMock = new Mock<Event>();

        EventsReactor reactor = SetupReactorWithConsoleLogger(Options.Create(new EventsReactorOptions() { EventRoundByMilliseconds = 200 }));

        using var cts = new CancellationTokenSource();

        reactor.EventReady += (sender, evt, timeDrift) =>
        {
            // test that sender is the same reactor instance, while we're here.
            Assert.AreEqual(reactor, sender);

            // check that event has a reference.
            Assert.IsNotNull(evt);

            if (evt == eventMock.Object)
            {
                eventFiredCounter++;
            }
        };

        // start the reactor.
        var reactorTask = reactor.RunAsync(cts.Token);

        // start testing.
        var testingTask = Task.Run(() =>
        {
            // push an event that shall be ready after some time...
            reactor.Push(eventMock.Object, fiveSecondsTimeSpan);

            Assert.AreEqual(ExpectedCounterValueBeforeRun, eventFiredCounter, $"Events counter does not match: Expected {ExpectedCounterValueBeforeRun}, got {eventFiredCounter}.");

            // now hurry the event...
            Assert.IsTrue(reactor.Hurry(eventMock.Object.Id, threeSecondsTimeSpan));

            // wait three seconds and check that the counter has gone up, since the event should have ran already.
            Task.Delay(threeSecondsTimeSpan).Wait();

            Assert.AreEqual(ExpectedCounterValueAfterHurry, eventFiredCounter, $"Events counter does not match: Expected {ExpectedCounterValueAfterHurry}, got {eventFiredCounter}.");
        });

        await testingTask.ContinueWith(p => cts.Cancel());
    }

    /// <summary>
    /// Checks that <see cref="EventsReactor.Expedite(Guid)"/> does what it should.
    /// </summary>
    /// <returns>A <see cref="Task"/> representing the asynchronous unit test.</returns>
    [TestMethod]
    public async Task Expedite_SingleEvent()
    {
        const int ExpectedCounterValueBeforeRun = 0;
        const int ExpectedCounterValueAfterRun = 1;

        TimeSpan fiveSecondsTimeSpan = TimeSpan.FromSeconds(5);
        TimeSpan twoSecondsTimeSpan = TimeSpan.FromSeconds(2);

        var eventFiredCounter = 0;
        var eventMock = new Mock<Event>();

        EventsReactor reactor = SetupReactorWithConsoleLogger(Options.Create(new EventsReactorOptions() { EventRoundByMilliseconds = 200 }));

        using var cts = new CancellationTokenSource();

        reactor.EventReady += (sender, evt, timeDrift) =>
        {
            // test that sender is the same reactor instance, while we're here.
            Assert.AreEqual(reactor, sender);

            // check that event has a reference.
            Assert.IsNotNull(evt);

            if (evt == eventMock.Object)
            {
                eventFiredCounter++;
            }
        };

        // start the reactor.
        var reactorTask = reactor.RunAsync(cts.Token);

        // start testing.
        var testingTask = Task.Run(() =>
        {
            // push an event that shall be ready after some time...
            reactor.Push(eventMock.Object, fiveSecondsTimeSpan);

            Assert.AreEqual(ExpectedCounterValueBeforeRun, eventFiredCounter, $"Events counter does not match: Expected {ExpectedCounterValueBeforeRun}, got {eventFiredCounter}.");

            // now expedite the event...
            reactor.Expedite(eventMock.Object.Id);

            // wait a bit and check that the counter has gone up, since the event should have ran already.
            Task.Delay(twoSecondsTimeSpan).Wait();

            Assert.AreEqual(ExpectedCounterValueAfterRun, eventFiredCounter, $"Events counter does not match: Expected {ExpectedCounterValueAfterRun}, got {eventFiredCounter}.");
        });

        await testingTask.ContinueWith(p => cts.Cancel());
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

        Event eventWithNoDelay = Mock.Of<Event>();
        Event eventWithDelay = Mock.Of<Event>();

        var reactor = SetupReactorWithConsoleLogger();

        var inmediateEventFiredCounter = 0;
        var delayedEventFiredCounter = 0;

        using var cts = new CancellationTokenSource();

        reactor.EventReady += (sender, evt, drift) =>
        {
            // test that sender is the same reactor instance, while we're here.
            Assert.AreEqual(reactor, sender);

            // check that event has a reference.
            Assert.IsNotNull(evt);

            if (evt == eventWithNoDelay)
            {
                inmediateEventFiredCounter++;
            }
            else if (evt == eventWithDelay)
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

        void OnEventReadyFunc(object sender, IEvent evt, TimeSpan timeDrift)
        {
            // test that sender is the same reactor instance, while we're here.
            Assert.AreEqual(reactor, sender);

            // check that event has a reference.
            Assert.IsNotNull(evt);

            if (!eventsDictionary.ContainsKey(evt.Id))
            {
                throw new InvalidOperationException($"Unknown event with id {evt.Id} received.");
            }

            eventsDictionary[evt.Id] = timeDrift;
        }

        reactor.EventReady += OnEventReadyFunc;

        using var cts = new CancellationTokenSource();

        // start the reactor.
        Task reactorTask = reactor.RunAsync(cts.Token);

        for (int i = 0; i < LoadSize; i++)
        {
            Event evt = Mock.Of<Event>();

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
        using var logFactory = LoggerFactory.Create(builder =>
        {
            builder.AddConsole(options => options.FormatterName = ConsoleFormatterNames.Systemd).SetMinimumLevel(LogLevel.Trace);
        });

        var logger = logFactory.CreateLogger<EventsReactor>();

        return new EventsReactor(logger, options ?? GoodReactorOptions);
    }
}

#pragma warning restore CA1806 // Do not ignore method results

