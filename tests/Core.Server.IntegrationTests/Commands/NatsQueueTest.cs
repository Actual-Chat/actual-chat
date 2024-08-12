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

        var testService = (ScheduledCommandTestService)services.GetRequiredService<IScheduledCommandTestService>();
        var commander = services.Commander();

        testService.ProcessedEvents.Count.Should().Be(0);
        await commander.Call(new AddTestEvent1Command(null));
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

        var testService = (ScheduledCommandTestService)services.GetRequiredService<IScheduledCommandTestService>();
        var countComputed = await Computed.Capture(
            () => testService.GetProcessedEventCount(default));

        testService.ProcessedEvents.Count.Should().Be(0);
        const int eventCount = 100;
        for (int i = 0; i < eventCount; i++)
            await queues.Enqueue(new TestEvent1(null));

        await DumpEventCount("after enqueue");
        await queues.WhenProcessing();

        await DumpEventCount("after queues.WhenProcessing");
        await countComputed.When(i => i >= eventCount).WaitAsync(TimeSpan.FromSeconds(10)).SilentAwait();

        await DumpEventCount($"after awaiting {eventCount} events");
        countComputed.Value.Should().BeGreaterThanOrEqualTo(eventCount);

        async Task DumpEventCount(string point)
        {
            countComputed = await countComputed.Update();
            Out.WriteLine($"{nameof(MultipleCommandsCanBeScheduled)}: event count {point}: {testService.ProcessedEvents.Count} (computed: {countComputed.Value})");
        }
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

        var testService = (ScheduledCommandTestService)services.GetRequiredService<IScheduledCommandTestService>();
        testService.ProcessedEvents.Count.Should().Be(0);

        await queues.Enqueue(new AddBothTestEventsCommandWithShardKey { ShardKey = 7 });
        await queues.WhenProcessing();

        testService.ProcessedEvents.Count.Should().Be(2);
    }
}
