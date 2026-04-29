using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using MediaBrowser.Controller.Configuration;
using MediaBrowser.Controller.LibraryTaskScheduler;
using MediaBrowser.Model.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace Jellyfin.Controller.Tests.LibraryTaskScheduler;

public class LimitedConcurrencyLibrarySchedulerTests
{
    [Fact]
    public async Task Enqueue_WhenNested_DoesNotGrowCallStack()
    {
        const int MaxDepth = 10_000;
        using var cancellationTokenSource = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        await using var scheduler = CreateScheduler(4);

        var deadlockDetector = (AsyncLocal<CancellationTokenSource>)typeof(LimitedConcurrencyLibraryScheduler)
            .GetField("_deadlockDetector", BindingFlags.NonPublic | BindingFlags.Static)!
            .GetValue(null)!;
        var previousDeadlockDetector = deadlockDetector.Value;
        deadlockDetector.Value = cancellationTokenSource;
        var actualDepth = 0;
        async Task ProcessItem(int value, IProgress<double> progress)
        {
            actualDepth = value;
            if (value < MaxDepth)
            {
                await scheduler.Enqueue(
                    [value + 1],
                    ProcessItem,
                    progress,
                    cancellationTokenSource.Token);
            }
        }

        try
        {
            await scheduler.Enqueue(
                [0],
                ProcessItem,
                new Progress<double>(),
                cancellationTokenSource.Token);

            Assert.Equal(MaxDepth, actualDepth);
        }
        finally
        {
            deadlockDetector.Value = previousDeadlockDetector!;
        }
    }

    private static LimitedConcurrencyLibraryScheduler CreateScheduler(int fanoutConcurrency)
    {
        var serverConfigurationManager = new Mock<IServerConfigurationManager>();
        serverConfigurationManager.Setup(manager => manager.Configuration)
            .Returns(new ServerConfiguration
            {
                LibraryScanFanoutConcurrency = fanoutConcurrency
            });

        var hostApplicationLifetime = new Mock<IHostApplicationLifetime>();
        hostApplicationLifetime.SetupGet(lifetime => lifetime.ApplicationStopping)
            .Returns(CancellationToken.None);

        return new LimitedConcurrencyLibraryScheduler(
            hostApplicationLifetime.Object,
            NullLogger<LimitedConcurrencyLibraryScheduler>.Instance,
            serverConfigurationManager.Object);
    }
}
