using ActualChat.Commands;
using ActualChat.Hosting;
using ActualChat.Nats.Module;
using ActualChat.Testing.Host;

namespace ActualChat.Core.Server.IntegrationTests.Commands;

[Collection(nameof(CommandsCollection)), Trait("Category", nameof(CommandsCollection))]
public class NatsCommandQueueTest(ITestOutputHelper @out)
    : AppHostTestBase($"x-{nameof(NatsCommandQueueTest)}", TestAppHostOptions.Default, @out)
{
    [Fact(Timeout = 20_000)]
    public async Task SmokeTest()
    {
        using var host = await NewAppHost(options => options with {
            InstanceName = $"x-{nameof(NatsCommandQueueTest)}-{nameof(SmokeTest)}",
            AppServicesExtender = (c, services) => {
                services
                    .AddCommandQueues(HostRole.OneBackendServer)
                    .AddFusion()
                    .AddService<ScheduledCommandTestService>();
            },
        });
        var services = host.Services;
        var scheduler = services.GetRequiredKeyedService<ShardCommandQueueScheduler>(HostRole.OneBackendServer.Id.Value);
        _ = scheduler.Run();

        var testService = services.GetRequiredService<ScheduledCommandTestService>();
        var commander = services.GetRequiredService<ICommander>();

        testService.ProcessedEvents.Count.Should().Be(0);
        await commander.Call(new TestCommand(null));

        await host.WaitForProcessingOfAlreadyQueuedCommands(TimeSpan.FromSeconds(2));

        testService.ProcessedEvents.Count.Should().BeGreaterThanOrEqualTo(1);
    }

    [Fact]
    public async Task MultipleCommandsCanBeScheduled()
    {
        using var host = await NewAppHost(options => options with {
            InstanceName = $"x-{nameof(NatsCommandQueueTest)}-{nameof(MultipleCommandsCanBeScheduled)}",
            AppServicesExtender = (c, services) => {
                services
                    .AddCommandQueues(HostRole.OneBackendServer)
                    .AddFusion()
                    .AddService<ScheduledCommandTestService>();
            },
        });
        var services = host.Services;
        var scheduler = services.GetRequiredKeyedService<ShardCommandQueueScheduler>(HostRole.OneBackendServer.Id.Value);
        _ = scheduler.Run();

        var testService = services.GetRequiredService<ScheduledCommandTestService>();
        var queues = services.GetRequiredService<ICommandQueues>();
        var countComputed = await Computed.Capture(() => testService.GetProcessedEventCount(CancellationToken.None));

        testService.ProcessedEvents.Count.Should().Be(0);
        for (int i = 0; i < 100; i++)
            await queues.Enqueue(new TestEvent(null));

        await host.WaitForProcessingOfAlreadyQueuedCommands(TimeSpan.FromSeconds(2));

        Out.WriteLine($"{nameof(MultipleCommandsCanBeScheduled)}: event count is {countComputed.Value}. Collection has {testService.ProcessedEvents.Count}");
        await countComputed.When(i => i >= 100).WaitAsync(TimeSpan.FromSeconds(10));

        testService.ProcessedEvents.Count.Should().BeGreaterThanOrEqualTo(100);
    }

    [Fact]
    public async Task CommandsWithCustomQueuesAreHandled()
    {
        using var host = await NewAppHost(options => options with {
            InstanceName = $"x-{nameof(NatsCommandQueueTest)}-{nameof(CommandsWithCustomQueuesAreHandled)}",
            AppServicesExtender = (c, services) => {
                services
                    // Remove all ShardCommandQueueScheduler to debug just one
                    // .RemoveAll(sd => sd.IsKeyedService)
                    // .RemoveAll(sd => sd.ServiceType == typeof(IHostedService))
                    .AddCommandQueues(HostRole.DefaultQueue)
                    .AddFusion()
                    .AddService<ScheduledCommandTestService>();
            },
            HostConfigurationExtender = cfg => {
                cfg.AddInMemory(("HostSettings:CommandQueueRoles", "DefaultQueue"));
            },
        });
        var services = host.Services;
        var queueScheduler = services.GetRequiredKeyedService<ShardCommandQueueScheduler>(HostRole.DefaultQueue.Id.Value);
        _ = queueScheduler.Run();

        var testService = services.GetRequiredService<ScheduledCommandTestService>();
        var queues = services.GetRequiredService<ICommandQueues>();
        testService.ProcessedEvents.Count.Should().Be(0);

        await queues.Enqueue(new TestCommand3 { ShardKey = 7 });

        await host.WaitForProcessingOfAlreadyQueuedCommands();

        testService.ProcessedEvents.Count.Should().Be(2);
    }
}
