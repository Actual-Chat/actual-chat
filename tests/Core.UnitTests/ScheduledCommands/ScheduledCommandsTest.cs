using ActualChat.ScheduledCommands;
using Microsoft.Extensions.Hosting;
using Stl.Time.Testing;

namespace ActualChat.Core.UnitTests.ScheduledCommands;

public class ScheduledCommandsTest: TestBase
{
    public ScheduledCommandsTest(ITestOutputHelper @out) : base(@out)
    { }

    [Fact]
    public async Task EnqueueEventOnCommandCompletion()
    {
        await using var services = new ServiceCollection()
            .AddFusion()
            .AddLocalEventScheduler()
            .AddComputeService<ScheduledCommandTestService>()
            .Services
            .BuildServiceProvider();

        var hostedServices = services.GetServices<IHostedService>().ToList();
        foreach (var hostedService in hostedServices)
            await hostedService.StartAsync(CancellationToken.None).ConfigureAwait(false);

        var testService = services.GetRequiredService<ScheduledCommandTestService>();
        var commander = services.GetRequiredService<ICommander>();

        testService.ProcessedEvents.Count.Should().Be(0);
        var commandTask = commander.Call(new ScheduledCommandTestService.TestCommand(null));
        testService.ProcessedEvents.Count.Should().Be(0);

        await commandTask.ConfigureAwait(false);
        var testClock = new TestClock();
        await testClock.Delay(500).ConfigureAwait(false);

        testService.ProcessedEvents.Count.Should().Be(1);
    }
}
