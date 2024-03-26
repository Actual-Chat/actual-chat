using ActualChat.Queues;
using ActualChat.Queues.InMemory;

namespace ActualChat.Core.UnitTests.Commands;

public class ScheduledCommandsTest(ITestOutputHelper @out) : TestBase(@out)
{
    [Fact]
    public async Task EnqueueEventOnCommandCompletion()
    {
        await using var services = CreateServices();
        await services.HostedServices().Start();

        var commander = services.Commander();
        var queues = services.Queues();
        queues.Should().BeAssignableTo<InMemoryQueues>();
        queues.WhenRunning.Should().NotBeNull();

        var testService = services.GetRequiredService<ScheduledCommandTestService>();
        testService.ProcessedEvents.Count.Should().Be(0);
        await commander.Call(new TestCommand(null));
        testService.ProcessedEvents.Count.Should().Be(0);

        await queues.WhenProcessing();
        testService.ProcessedEvents.Count.Should().Be(1);
    }

    [Fact]
    public async Task MultipleEventHandlersAreCalled()
    {
        await using var services = CreateServices(services => {
            var fusion = services.AddFusion();
            fusion.AddService<DedicatedEventHandler>();
            services.AddSingleton<DedicatedInterfaceEventHandler>();
            fusion.Commander.AddHandlers<DedicatedInterfaceEventHandler>();
        });
        await services.HostedServices().Start();
        var commander = services.Commander();
        var queues = services.Queues();

        var testService = services.GetRequiredService<ScheduledCommandTestService>();
        testService.ProcessedEvents.Count.Should().Be(0);
        await commander.Call(new TestCommand2());

        await queues.WhenProcessing();
        foreach (var eventCommand in testService.ProcessedEvents)
            Out.WriteLine(eventCommand.ToString());

        testService.ProcessedEvents.Count.Should().Be(3);
    }

    [Fact]
    public async Task MultipleQueuesDontLeadToDuplicateEvents()
    {
        await using var services = CreateServices(services => {
            var fusion = services.AddFusion();
            fusion.AddService<DedicatedEventHandler>();
            services.AddSingleton<DedicatedInterfaceEventHandler>();
            fusion.Commander.AddHandlers<DedicatedInterfaceEventHandler>();
        });
        await services.HostedServices().Start();
        var commander = services.Commander();
        var queues = services.Queues();

        var testService = services.GetRequiredService<ScheduledCommandTestService>();
        testService.ProcessedEvents.Count.Should().Be(0);
        await commander.Call(new TestCommand3());

        await queues.WhenProcessing();
        foreach (var eventCommand in testService.ProcessedEvents)
            Out.WriteLine(eventCommand.ToString());

        testService.ProcessedEvents.Count.Should().Be(3);
    }

    private ServiceProvider CreateServices(Action<IServiceCollection>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddTestHostInfo();
        services.AddTestLogging(Out);
        services.AddInMemoryQueues();
        var fusion = services.AddFusion();
        fusion.AddService<ScheduledCommandTestService>();

        configure?.Invoke(services);
        return services.BuildServiceProvider();
    }
}
