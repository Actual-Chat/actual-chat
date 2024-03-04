using ActualChat.Commands;
using ActualChat.Commands.Internal;
using ActualChat.Hosting;

namespace ActualChat.Core.UnitTests.Commands;

public class ScheduledCommandsTest(ITestOutputHelper @out) : TestBase(@out)
{
    private static readonly QueueId _queueId = new (HostRole.OneBackendServer, 0);

    [Fact]
    public async Task EnqueueEventOnCommandCompletion()
    {
        await using var services = new ServiceCollection()
            .AddLogging()
            .AddLocalCommandQueues()
            .AddFusion()
            .AddService<ScheduledCommandTestService>()
            .Services
            .BuildServiceProvider();
        await services.HostedServices().Start();

        var queue = (LocalCommandQueue)services.GetRequiredService<ICommandQueues>()[_queueId];
        var testService = services.GetRequiredService<ScheduledCommandTestService>();
        var commander = services.GetRequiredService<ICommander>();

        testService.ProcessedEvents.Count.Should().Be(0);
        await commander.Call(new TestCommand(null));
        testService.ProcessedEvents.Count.Should().Be(0);

        await Awaiter.WaitFor(() => queue.SuccessCount != 0);

        testService.ProcessedEvents.Count.Should().Be(1);
    }

    [Fact]
    public async Task MultipleEventHandlersAreCalled()
    {
        await using var services = new ServiceCollection()
            .AddLogging()
            .AddLocalCommandQueues()
            .AddSingleton<DedicatedInterfaceEventHandler>()
            .AddCommander(c => c.AddHandlers<DedicatedInterfaceEventHandler>())
            .AddFusion()
            .AddService<ScheduledCommandTestService>()
            .AddService<DedicatedEventHandler>()
            .Services
            .BuildServiceProvider();
        await services.HostedServices().Start();

        var queue = (LocalCommandQueue)services.GetRequiredService<ICommandQueues>()[_queueId];
        var testService = services.GetRequiredService<ScheduledCommandTestService>();
        var commander = services.GetRequiredService<ICommander>();

        testService.ProcessedEvents.Count.Should().Be(0);
        await commander.Call(new TestCommand2());

        await Awaiter.WaitFor(() => queue.SuccessCount == 2);

        foreach (var eventCommand in testService.ProcessedEvents)
            Out.WriteLine(eventCommand.ToString());

        testService.ProcessedEvents.Count.Should().Be(3);
    }

    [Fact]
    public async Task MultipleQueuesDontLeadToDuplicateEvents()
    {
        await using var services = new ServiceCollection()
            .AddLogging()
            .AddLocalCommandQueues()
            .AddSingleton<DedicatedInterfaceEventHandler>()
            .AddCommander(c => c.AddHandlers<DedicatedInterfaceEventHandler>())
            .AddFusion()
            .AddService<ScheduledCommandTestService>()
            .AddService<DedicatedEventHandler>()
            .Services
            .BuildServiceProvider();
        await services.HostedServices().Start();

        var queue = (LocalCommandQueue)services.GetRequiredService<ICommandQueues>()[_queueId];
        var testService = services.GetRequiredService<ScheduledCommandTestService>();
        var commander = services.GetRequiredService<ICommander>();

        testService.ProcessedEvents.Count.Should().Be(0);
        await commander.Call(new TestCommand3());

        await Awaiter.WaitFor(() => queue.SuccessCount == 2);

        foreach (var eventCommand in testService.ProcessedEvents)
            Out.WriteLine(eventCommand.ToString());

        testService.ProcessedEvents.Count.Should().Be(3);
    }
}
