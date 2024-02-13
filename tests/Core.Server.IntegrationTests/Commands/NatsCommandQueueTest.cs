using ActualChat.Commands;
using ActualChat.Testing.Host;

namespace ActualChat.Core.Server.IntegrationTests.Commands;

[Collection(nameof(CommandsCollection)), Trait("Category", nameof(CommandsCollection))]
public class NatsCommandQueueTest(ITestOutputHelper @out)
    : AppHostTestBase($"x-{nameof(NatsCommandQueueTest)}", TestAppHostOptions.Default, @out)
{
    [Fact(Timeout = 10_000)]
    public async Task SmokeTest()
    {
        using var host = await NewAppHost(options => options with  {
            AppServicesExtender = (c, services) => {
                services
                    .AddNatsCommandQueues()
                    .AddFusion()
                    .AddService<ScheduledCommandTestService>();
            },
        });
        var services = host.Services;
        var scheduler = services.GetRequiredService<ShardCommandQueueScheduler>();
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
        using var host = await NewAppHost(options => options with  {
            AppServicesExtender = (c, services) => {
                services
                    .AddNatsCommandQueues()
                    .AddFusion()
                    .AddService<ScheduledCommandTestService>();
            },
        });
        var services = host.Services;
        var scheduler = services.GetRequiredService<ShardCommandQueueScheduler>();
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
}
