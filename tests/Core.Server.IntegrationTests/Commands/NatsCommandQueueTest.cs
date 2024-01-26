using ActualChat.Commands;
using ActualChat.Testing.Host;

namespace ActualChat.Core.Server.IntegrationTests.Commands;

public class NatsCommandQueueTest(ITestOutputHelper @out) : AppHostTestBase(@out)
{
    [Fact]
    public async Task SmokeTest()
    {
        using var host = await NewAppHost(new TestAppHostOptions {
            AppServicesExtender = (c, services) => {
                services
                    .AddNatsCommandQueues()
                    .AddFusion()
                    .AddService<ScheduledCommandTestService>();
            },
        });
        var services = host.Services;
        var scheduler = services.GetRequiredService<ShardCommandQueueScheduler>();
        _ = scheduler.Run();

        var testService = services.GetRequiredService<ScheduledCommandTestService>();
        var commander = services.GetRequiredService<ICommander>();

        testService.ProcessedEvents.Count.Should().Be(0);
        await commander.Call(new TestCommand(null));
        testService.ProcessedEvents.Count.Should().Be(0);

        await Task.Delay(2000);

        testService.ProcessedEvents.Count.Should().BeGreaterThanOrEqualTo(1);
    }
}
