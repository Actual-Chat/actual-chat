using ActualChat.Queues;
using ActualChat.Queues.InMemory;

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

        var queues = services.Queues();
        queues.Should().BeAssignableTo<InMemoryQueues>();
        var testService = services.GetRequiredService<ScheduledCommandTestService>();
        var commander = services.Commander();

        testService.ProcessedEvents.Count.Should().Be(0);
        await commander.Call(new TestCommand(null));
        testService.ProcessedEvents.Count.Should().Be(0);

        await queues.WhenProcessing();
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

        var queues = services.Queues();
        queues.Should().BeAssignableTo<InMemoryQueues>();
        var testService = services.GetRequiredService<ScheduledCommandTestService>();
        var commander = services.Commander();

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

        var queues = services.Queues();
        queues.Should().BeAssignableTo<InMemoryQueues>();
        var testService = services.GetRequiredService<ScheduledCommandTestService>();
        var commander = services.Commander();

        testService.ProcessedEvents.Count.Should().Be(0);
        await commander.Call(new TestCommand3());

        await queues.WhenProcessing();
        foreach (var eventCommand in testService.ProcessedEvents)
            Out.WriteLine(eventCommand.ToString());

        testService.ProcessedEvents.Count.Should().Be(3);
    }
}
