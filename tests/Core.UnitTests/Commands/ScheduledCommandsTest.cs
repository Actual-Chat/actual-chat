using ActualChat.Commands;
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

        var testService = services.GetRequiredService<ScheduledCommandTestService>();
        var commander = services.GetRequiredService<ICommander>();

        testService.ProcessedEvents.Count.Should().Be(0);
        await commander.Call(new TestCommand(null));
        testService.ProcessedEvents.Count.Should().Be(0);

        await Task.Delay(500);
        testService.ProcessedEvents.Count.Should().Be(1);
    }

    [Fact]
    public async Task MultipleEventHandlersAreCalled()
    {
        await using var services = new ServiceCollection()
            .AddLogging()
            .AddCommander()
            .AddEventHandlers()
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

        var testService = services.GetRequiredService<ScheduledCommandTestService>();
        var commander = services.GetRequiredService<ICommander>();

        testService.ProcessedEvents.Count.Should().Be(0);
        await commander.Call(new TestCommand2());

        await Awaiter.WaitFor(() => testService.ProcessedEvents.Count == 2);
        testService.ProcessedEvents.Count.Should().Be(2);
    }
}
