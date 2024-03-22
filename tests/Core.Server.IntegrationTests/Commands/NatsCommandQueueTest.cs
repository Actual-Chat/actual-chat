using ActualChat.Hosting;
using ActualChat.Queues;
using ActualChat.Queues.Internal;
using ActualChat.Queues.Nats;
using ActualChat.Testing.Host;

namespace ActualChat.Core.Server.IntegrationTests.Commands;

[Collection(nameof(CommandsCollection)), Trait("Category", nameof(CommandsCollection))]
public class NatsQueueTest(ITestOutputHelper @out)
    : AppHostTestBase($"x-{nameof(NatsQueueTest)}", TestAppHostOptions.Default, @out)
{
    [Fact(Timeout = 10_000)]
    public async Task SmokeTest()
    {
        using var host = await NewAppHost(options => options with {
            InstanceName = $"x-{nameof(NatsQueueTest)}-{nameof(SmokeTest)}",
            AppServicesExtender = (c, services) => {
                services
                    .AddNatsQueues()
                    .AddFusion()
                    .AddService<ScheduledCommandTestService>();
            },
        });
        var services = host.Services;
        var queues = services.GetRequiredService<NatsQueues>().Start();

        var testService = services.GetRequiredService<ScheduledCommandTestService>();
        var commander = services.GetRequiredService<ICommander>();
        var countComputed = await Computed.Capture(() => testService.GetProcessedEventCount(CancellationToken.None));

        testService.ProcessedEvents.Count.Should().Be(0);
        await commander.Call(new TestCommand(null));
        await queues.WhenProcessing();
        await countComputed.WhenInvalidated();

        testService.ProcessedEvents.Count.Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task MultipleCommandsCanBeScheduled()
    {
        using var host = await NewAppHost(options => options with {
            InstanceName = $"x-{nameof(NatsQueueTest)}-{nameof(MultipleCommandsCanBeScheduled)}",
            AppServicesExtender = (c, services) => {
                services
                    .AddNatsQueues()
                    .AddFusion()
                    .AddService<ScheduledCommandTestService>();
            },
        });
        var services = host.Services;
        var queues = services.GetRequiredService<NatsQueues>().Start();

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
            AppServicesExtender = (c, services) => {
                services
                    // Remove all ShardCommandQueueScheduler to debug just one
                    // .RemoveAll(sd => sd.IsKeyedService)
                    // .RemoveAll(sd => sd.ServiceType == typeof(IHostedService))
                    .AddNatsQueues()
                    .AddFusion()
                    .AddService<ScheduledCommandTestService>();
            },
        });
        var services = host.Services;
        var queues = services.GetRequiredService<NatsQueues>().Start();

        var testService = services.GetRequiredService<ScheduledCommandTestService>();
        testService.ProcessedEvents.Count.Should().Be(0);

        await queues.Enqueue(new TestCommand3 { ShardKey = 7 });
        await queues.WhenProcessing();

        testService.ProcessedEvents.Count.Should().Be(2);
    }
}
