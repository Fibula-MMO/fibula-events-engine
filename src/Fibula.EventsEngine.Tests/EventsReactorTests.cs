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
using System.Threading;
using System.Threading.Tasks;
using Fibula.Utilities.Testing;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;

/// <summary>
/// Tests for the <see cref="EventsReactor"/> class.
/// </summary>
[TestClass]
public class EventsReactorTests
{
    /// <summary>
    /// Checks <see cref="EventsReactor"/> initialization.
    /// </summary>
    [TestMethod]
    public void EventsReactor_Initialization()
    {
        var loggerMock = Mock.Of<ILogger<EventsReactor>>();

        // Supplying no logger instance should throw.
        ExceptionAssert.Throws<ArgumentNullException>(() => new EventsReactor(null), "Value cannot be null. (Parameter 'logger')");

        // use a non default reference time.
        new EventsReactor(loggerMock);
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

        TimeSpan overheadDelay = TimeSpan.FromMilliseconds(100);
        TimeSpan twoSecondsTimeSpan = TimeSpan.FromSeconds(2);
        TimeSpan threeSecondsTimeSpan = TimeSpan.FromSeconds(3);

        var eventFiredCounter = 0;

        Mock<BaseEvent> eventMock = new Mock<BaseEvent>();

        eventMock.SetupGet(e => e.CanBeCancelled).Returns(true);

        EventsReactor reactor = this.SetupReactorWithConsoleLogger();

        using CancellationTokenSource cts = new CancellationTokenSource();

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

        var reactor = this.SetupReactorWithConsoleLogger();

        var inmediateEventFiredCounter = 0;
        var delayedEventFiredCounter = 0;

        using CancellationTokenSource cts = new CancellationTokenSource();

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

        // push an event with a delay of some seconds.
        reactor.Push(eventWithDelay, twoSecondsTimeSpan);

        // delay for 100 ms (to account for setup overhead and multi threading) and check that the counter has NOT gone up for delayed events.
        await Task.Delay(overheadDelay).ContinueWith(prev =>
        {
            Assert.AreEqual(ExpectedCounterValueBeforeRun, delayedEventFiredCounter, $"Delayed events counter does not match: Expected {ExpectedCounterValueBeforeRun}, got {delayedEventFiredCounter}.");
        });

        // push an event with no delay.
        reactor.Push(eventWithNoDelay);

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
    /// Helper method used to setup a <see cref="EventsReactor"/> instance with a console logger.
    /// </summary>
    /// <returns>The reactor instance.</returns>
    private EventsReactor SetupReactorWithConsoleLogger()
    {
        using var logFactory = LoggerFactory.Create(builder => builder.AddConsole().SetMinimumLevel(LogLevel.Trace));

        var logger = logFactory.CreateLogger<EventsReactor>();

        return new EventsReactor(logger);
    }
}
