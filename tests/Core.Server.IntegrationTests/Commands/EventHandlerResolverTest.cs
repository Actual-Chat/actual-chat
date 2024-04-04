using ActualChat.Queues.Internal;
using ActualChat.Testing.Host;

namespace ActualChat.Core.Server.IntegrationTests.Commands;

[Collection(nameof(CommandsCollection)), Trait("Category", nameof(CommandsCollection))]
public class EventHandlerRegistryTest(ITestOutputHelper @out)
    : AppHostTestBase($"x-{nameof(EventHandlerRegistryTest)}", TestAppHostOptions.Default, @out)
{
    [Fact(Timeout = 20_000)]
    public async Task BackendServerRoleShouldHandleAllEvents()
    {
        using var host = await NewAppHost(options => options with  {
            ConfigureServices = (ctx, services) => {
                var rpcHost = services.AddRpcHost(ctx.HostInfo);
                rpcHost.AddBackend<IScheduledCommandTestService, ScheduledCommandTestService>();
            },
        });
        var services = host.Services;
        services.Queues().Start();

        var eventHandlerResolver = services.GetRequiredService<EventHandlerRegistry>();
        var eventHandlers = eventHandlerResolver.AllEventHandlers;
        eventHandlers.Should().NotBeEmpty();
    }
}
