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
            .AddFusion()
            .AddLocalCommandScheduler(Queues.Default)
            .AddComputeService<ScheduledCommandTestService>()
            .Services
            .BuildServiceProvider();
        await services.HostedServices().Start();

        var queue = services.GetRequiredService<ICommandQueues>().Get(Queues.Default) as LocalCommandQueue;
        var testService = services.GetRequiredService<ScheduledCommandTestService>();
        var commander = services.GetRequiredService<ICommander>();

        testService.ProcessedEvents.Count.Should().Be(0);
        await commander.Call(new TestCommand(null));
        testService.ProcessedEvents.Count.Should().Be(0);

        await Awaiter.WaitFor(() => queue!.CompletedCommandCount != 0);

        testService.ProcessedEvents.Count.Should().Be(1);
    }

    [Fact]
    public async Task MultipleEventHandlersAreCalled()
    {
        await using var services = new ServiceCollection()
            .AddLogging()
            .AddCommander()
            .AddHandlers<DedicatedInterfaceEventHandler>()
            .Services
            .AddSingleton<DedicatedInterfaceEventHandler>()
            .AddFusion()
            .AddLocalCommandScheduler(Queues.Default)
            .AddComputeService<ScheduledCommandTestService>()
            .AddComputeService<DedicatedEventHandler>()
            .Services
            .BuildServiceProvider();
        await services.HostedServices().Start();

        var queue = services.GetRequiredService<ICommandQueues>().Get(Queues.Default) as LocalCommandQueue;
        var testService = services.GetRequiredService<ScheduledCommandTestService>();
        var commander = services.GetRequiredService<ICommander>();

        testService.ProcessedEvents.Count.Should().Be(0);
        await commander.Call(new TestCommand2());

        await Awaiter.WaitFor(() => queue!.CompletedCommandCount == 2);
        // await Awaiter.WaitFor(() =>  testService.ProcessedEvents.Count == 3);

        await Task.Delay(500);

        foreach (var @event in testService.ProcessedEvents)
            Out.WriteLine(@event.ToString());

        testService.ProcessedEvents.Count.Should().Be(3);
    }

    [Fact]
    public async Task MultipleQueuesDontLeadToDuplicateEvents()
    {
        await using var services = new ServiceCollection()
            .AddLogging()
            .AddCommander()
            .AddHandlers<DedicatedInterfaceEventHandler>()
            .Services
            .AddSingleton<DedicatedInterfaceEventHandler>()
            .AddFusion()
            .AddLocalCommandScheduler(Queues.Default)
            .AddComputeService<ScheduledCommandTestService>()
            .AddComputeService<DedicatedEventHandler>()
            .Services
            .BuildServiceProvider();
        await services.HostedServices().Start();

        var queue = services.GetRequiredService<ICommandQueues>().Get(Queues.Default) as LocalCommandQueue;
        var testService = services.GetRequiredService<ScheduledCommandTestService>();
        var commander = services.GetRequiredService<ICommander>();

        testService.ProcessedEvents.Count.Should().Be(0);
        await commander.Call(new TestCommand3());

        await Awaiter.WaitFor(() => queue!.CompletedCommandCount == 2);
        await Task.Delay(500);

        foreach (var @event in testService.ProcessedEvents)
            Out.WriteLine(@event.ToString());

        testService.ProcessedEvents.Count.Should().Be(3);
    }
}
