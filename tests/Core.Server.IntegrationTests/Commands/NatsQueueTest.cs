using ActualChat.Queues;
using ActualChat.Queues.Nats;
using ActualChat.Testing.Host;

namespace ActualChat.Core.Server.IntegrationTests.Commands;

[Collection(nameof(CommandsCollection)), Trait("Category", nameof(CommandsCollection))]
public class NatsQueueTest(ITestOutputHelper @out)
    : AppHostTestBase($"x-{nameof(NatsQueueTest)}", TestAppHostOptions.Default, @out)
{
    [Fact(Timeout = 20_000)]
    public async Task SmokeTest()
    {
        using var host = await NewAppHost(options => options with {
            InstanceName = $"x-{nameof(NatsQueueTest)}-{nameof(SmokeTest)}",
            ConfigureServices = (ctx, services) => {
                var rpcHost = services.AddRpcHost(ctx.HostInfo);
                rpcHost.AddBackend<IScheduledCommandTestService, ScheduledCommandTestService>();
            },
            DbInitializeOptions = new() { InitializeData = false },
            UseNatsQueues = true,
            MustCleanupRedis = true,
        });
        var services = host.Services;
        var queues = services.Queues();
        queues.Should().BeAssignableTo<NatsQueues>();

        var testService = services.GetRequiredService<ScheduledCommandTestService>();
        var commander = services.Commander();

        testService.ProcessedEvents.Count.Should().Be(0);
        await commander.Call(new TestCommand(null));
        await queues.WhenProcessing();

        testService.ProcessedEvents.Count.Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task MultipleCommandsCanBeScheduled()
    {
        using var host = await NewAppHost(options => options with {
            InstanceName = $"x-{nameof(NatsQueueTest)}-{nameof(MultipleCommandsCanBeScheduled)}",
            ConfigureServices = (ctx, services) => {
                var rpcHost = services.AddRpcHost(ctx.HostInfo);
                rpcHost.AddBackend<IScheduledCommandTestService, ScheduledCommandTestService>();
            },
            DbInitializeOptions = new() { InitializeData = false },
            UseNatsQueues = true,
            MustCleanupRedis = true,
        });
        var services = host.Services;
        var queues = services.Queues();
        queues.Should().BeAssignableTo<NatsQueues>();

        var testService = services.GetRequiredService<ScheduledCommandTestService>();
        var countComputed = await Computed.Capture(() => testService.GetProcessedEventCount(CancellationToken.None));

        testService.ProcessedEvents.Count.Should().Be(0);
        for (int i = 0; i < 100; i++)
            await queues.Enqueue(new TestEvent(null));

        await queues.WhenProcessing();

        Out.WriteLine($"{nameof(MultipleCommandsCanBeScheduled)}: event count is {countComputed.Value}. Collection has {testService.ProcessedEvents.Count}");
        await countComputed.When(i => i >= 100).WaitAsync(TimeSpan.FromSeconds(10));

        testService.ProcessedEvents.Count.Should().BeGreaterThanOrEqualTo(100);
    }

    [Fact]
    public async Task CommandsWithCustomQueuesAreHandled()
    {
        using var host = await NewAppHost(options => options with {
            InstanceName = $"x-{nameof(NatsQueueTest)}-{nameof(CommandsWithCustomQueuesAreHandled)}",
            ConfigureServices = (ctx, services) => {
                var rpcHost = services.AddRpcHost(ctx.HostInfo);
                rpcHost.AddBackend<IScheduledCommandTestService, ScheduledCommandTestService>();
            },
            DbInitializeOptions = new() { InitializeData = false },
            UseNatsQueues = true,
            MustCleanupRedis = true,
        });
        var services = host.Services;
        var queues = services.Queues();
        queues.Should().BeAssignableTo<NatsQueues>();

        var testService = services.GetRequiredService<ScheduledCommandTestService>();
        testService.ProcessedEvents.Count.Should().Be(0);

        await queues.Enqueue(new TestCommand3 { ShardKey = 7 });
        await queues.WhenProcessing();

        testService.ProcessedEvents.Count.Should().Be(2);
    }
}
