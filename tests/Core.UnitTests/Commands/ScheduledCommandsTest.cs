using ActualChat.Commands;
using ActualChat.Commands.Internal;
using ActualChat.Testing.Collections;

namespace ActualChat.Core.UnitTests.Commands;

[Collection(nameof(AppHostTests)), Trait("Category", nameof(AppHostTests))]
public class ScheduledCommandsTest: TestBase
{
    public ScheduledCommandsTest(ITestOutputHelper @out) : base(@out)
    { }

    [Fact]
    public async Task EnqueueEventOnCommandCompletion()
    {
        await using var services = new ServiceCollection()
            .AddLogging()
            .AddLocalCommandQueues()
            .AddCommandQueueScheduler(Queues.Default.Name)
            .AddFusion()
            .AddComputeService<ScheduledCommandTestService>()
            .Services
            .BuildServiceProvider();
        await services.HostedServices().Start();

        var queue = (LocalCommandQueue)services.GetRequiredService<ICommandQueues>()[Queues.Default];
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
            .AddCommandQueueScheduler(Queues.Default.Name)
            .AddSingleton<DedicatedInterfaceEventHandler>()
            .AddCommander(c => c.AddHandlers<DedicatedInterfaceEventHandler>())
            .AddFusion()
            .AddComputeService<ScheduledCommandTestService>()
            .AddComputeService<DedicatedEventHandler>()
            .Services
            .BuildServiceProvider();
        await services.HostedServices().Start();

        var queue = (LocalCommandQueue)services.GetRequiredService<ICommandQueues>()[Queues.Default];
        var testService = services.GetRequiredService<ScheduledCommandTestService>();
        var commander = services.GetRequiredService<ICommander>();

        testService.ProcessedEvents.Count.Should().Be(0);
        await commander.Call(new TestCommand2());

        await Awaiter.WaitFor(() => queue.SuccessCount == 2);

        foreach (var @event in testService.ProcessedEvents)
            Out.WriteLine(@event.ToString());

        testService.ProcessedEvents.Count.Should().Be(3);
    }

    [Fact]
    public async Task MultipleQueuesDontLeadToDuplicateEvents()
    {
        await using var services = new ServiceCollection()
            .AddLogging()
            .AddLocalCommandQueues()
            .AddCommandQueueScheduler(Queues.Default.Name)
            .AddSingleton<DedicatedInterfaceEventHandler>()
            .AddCommander(c => c.AddHandlers<DedicatedInterfaceEventHandler>())
            .AddFusion()
            .AddComputeService<ScheduledCommandTestService>()
            .AddComputeService<DedicatedEventHandler>()
            .Services
            .BuildServiceProvider();
        await services.HostedServices().Start();

        var queue = (LocalCommandQueue)services.GetRequiredService<ICommandQueues>()[Queues.Default];
        var testService = services.GetRequiredService<ScheduledCommandTestService>();
        var commander = services.GetRequiredService<ICommander>();

        testService.ProcessedEvents.Count.Should().Be(0);
        await commander.Call(new TestCommand3());

        await Awaiter.WaitFor(() => queue.SuccessCount == 2);

        foreach (var @event in testService.ProcessedEvents)
            Out.WriteLine(@event.ToString());

        testService.ProcessedEvents.Count.Should().Be(3);
    }
}
