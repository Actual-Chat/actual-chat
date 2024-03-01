using ActualChat.Commands;
using ActualChat.Hosting;
using ActualChat.Testing.Host;

namespace ActualChat.Core.Server.IntegrationTests.Commands;

[Collection(nameof(CommandsCollection)), Trait("Category", nameof(CommandsCollection))]
public class NatsCommandQueueTest(ITestOutputHelper @out)
    : AppHostTestBase($"x-{nameof(NatsCommandQueueTest)}", TestAppHostOptions.Default, @out)
{
    [Fact(Timeout = 1000_000)]
    public async Task SmokeTest()
    {
        using var host = await NewAppHost(options => options with {
            AppServicesExtender = (c, services) => {
                services
                    .AddCommandQueues(HostRole.BackendServer)
                    .AddFusion()
                    .AddService<ScheduledCommandTestService>();
            },
        });
        var services = host.Services;
        var scheduler = services.GetRequiredKeyedService<ShardCommandQueueScheduler>(HostRole.BackendServer.Id.Value);
        _ = scheduler.Run();

        var testService = services.GetRequiredService<ScheduledCommandTestService>();
        var commander = services.GetRequiredService<ICommander>();
        var countComputed = await Computed.Capture(() => testService.GetProcessedEventCount(CancellationToken.None));

        testService.ProcessedEvents.Count.Should().Be(0);
        await commander.Call(new TestCommand(null));

        await countComputed.WhenInvalidated();

        testService.ProcessedEvents.Count.Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task MultipleCommandsCanBeScheduled()
    {
        using var host = await NewAppHost(options => options with {
            AppServicesExtender = (c, services) => {
                services
                    .AddCommandQueues(HostRole.BackendServer)
                    .AddFusion()
                    .AddService<ScheduledCommandTestService>();
            },
        });
        var services = host.Services;
        var scheduler = services.GetRequiredKeyedService<ShardCommandQueueScheduler>(HostRole.BackendServer.Id.Value);
        _ = scheduler.Run();

        var testService = services.GetRequiredService<ScheduledCommandTestService>();
        var queues = services.GetRequiredService<ICommandQueues>();
        var countComputed = await Computed.Capture(() => testService.GetProcessedEventCount(CancellationToken.None));

        testService.ProcessedEvents.Count.Should().Be(0);
        for (int i = 0; i < 100; i++)
            await queues.Enqueue(new TestEvent(null));

        await countComputed.When(i => i >= 100).WaitAsync(TimeSpan.FromSeconds(10));

        testService.ProcessedEvents.Count.Should().BeGreaterThanOrEqualTo(100);
    }

    [Fact]
    public async Task CommandsWithCustomQueuesAreHandled()
    {
        using var host = await NewAppHost(options => options with {
            AppServicesExtender = (c, services) => {
                services
                    // .AddCommandQueues(HostRole.BackendServer)
                    .AddCommandQueues(HostRole.DefaultQueue)
                    .AddFusion()
                    .AddService<ScheduledCommandTestService>();
            },
        });
        var services = host.Services;
        // var scheduler = services.GetRequiredKeyedService<ShardCommandQueueScheduler>(HostRole.BackendServer.Id.Value);
        var queueScheduler = services.GetRequiredKeyedService<ShardCommandQueueScheduler>(HostRole.DefaultQueue.Id.Value);
        // _ = scheduler.Run();
        _ = queueScheduler.Run();

        var testService = services.GetRequiredService<ScheduledCommandTestService>();
        var queues = services.GetRequiredService<ICommandQueues>();
        var countComputed = await Computed.Capture(() => testService.GetProcessedEventCount(CancellationToken.None));

        testService.ProcessedEvents.Count.Should().Be(0);
        await queues.Enqueue(new TestCommand3 { ShardKey = 7 });

        await countComputed.When(i => i >= 1).WaitAsync(TimeSpan.FromSeconds(10));
        //
        // testService.ProcessedEvents.Count.Should().BeGreaterThanOrEqualTo(100);
    }
}
