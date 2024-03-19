using ActualChat.Queues;
using ActualChat.Queues.InMemory;
using ActualChat.Queues.Internal;

namespace ActualChat.Core.UnitTests.Commands;

public class ScheduledCommandsTest(ITestOutputHelper @out) : TestBase(@out)
{
    [Fact]
    public async Task EnqueueEventOnCommandCompletion()
    {
        await using var services = new ServiceCollection()
            .AddLogging()
            .AddInMemoryQueues()
            .AddFusion()
            .AddService<ScheduledCommandTestService>()
            .Services
            .BuildServiceProvider();
        await services.HostedServices().Start();

        services.GetRequiredService<IQueues>().Should().BeAssignableTo<InMemoryQueues>();
        var testService = services.GetRequiredService<ScheduledCommandTestService>();
        var commander = services.GetRequiredService<ICommander>();
        var scheduler = services.GetRequiredService<IQueueProcessor>();

        testService.ProcessedEvents.Count.Should().Be(0);
        await commander.Call(new TestCommand(null));
        testService.ProcessedEvents.Count.Should().Be(0);

        await scheduler.WhenProcessing(TimeSpan.FromSeconds(3), CancellationToken.None);

        testService.ProcessedEvents.Count.Should().Be(1);
    }

    [Fact]
    public async Task MultipleEventHandlersAreCalled()
    {
        await using var services = new ServiceCollection()
            .AddLogging()
            .AddInMemoryQueues()
            .AddSingleton<DedicatedInterfaceEventHandler>()
            .AddCommander(c => c.AddHandlers<DedicatedInterfaceEventHandler>())
            .AddFusion()
            .AddService<ScheduledCommandTestService>()
            .AddService<DedicatedEventHandler>()
            .Services
            .BuildServiceProvider();
        await services.HostedServices().Start();

        services.GetRequiredService<IQueues>().Should().BeAssignableTo<InMemoryQueues>();
        var testService = services.GetRequiredService<ScheduledCommandTestService>();
        var commander = services.GetRequiredService<ICommander>();
        var scheduler = services.GetRequiredService<IQueueProcessor>();

        testService.ProcessedEvents.Count.Should().Be(0);
        await commander.Call(new TestCommand2());

        await scheduler.WhenProcessing(TimeSpan.FromSeconds(3), CancellationToken.None);

        foreach (var eventCommand in testService.ProcessedEvents)
            Out.WriteLine(eventCommand.ToString());

        testService.ProcessedEvents.Count.Should().Be(3);
    }

    [Fact]
    public async Task MultipleQueuesDontLeadToDuplicateEvents()
    {
        await using var services = new ServiceCollection()
            .AddLogging()
            .AddInMemoryQueues()
            .AddSingleton<DedicatedInterfaceEventHandler>()
            .AddCommander(c => c.AddHandlers<DedicatedInterfaceEventHandler>())
            .AddFusion()
            .AddService<ScheduledCommandTestService>()
            .AddService<DedicatedEventHandler>()
            .Services
            .BuildServiceProvider();
        await services.HostedServices().Start();

        services.GetRequiredService<IQueues>().Should().BeAssignableTo<InMemoryQueues>();
        var testService = services.GetRequiredService<ScheduledCommandTestService>();
        var commander = services.GetRequiredService<ICommander>();
        var scheduler = services.GetRequiredService<IQueueProcessor>();

        testService.ProcessedEvents.Count.Should().Be(0);
        await commander.Call(new TestCommand3());

        await scheduler.WhenProcessing(TimeSpan.FromSeconds(3), CancellationToken.None);

        foreach (var eventCommand in testService.ProcessedEvents)
            Out.WriteLine(eventCommand.ToString());

        testService.ProcessedEvents.Count.Should().Be(3);
    }
}
