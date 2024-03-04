using ActualChat.Commands;
using ActualChat.Hosting;
using ActualChat.Nats.Module;
using ActualChat.Testing.Host;

namespace ActualChat.Core.Server.IntegrationTests.Commands;

[Collection(nameof(CommandsCollection)), Trait("Category", nameof(CommandsCollection))]
public class EventHandlerResolverTest(ITestOutputHelper @out)
    : AppHostTestBase($"x-{nameof(EventHandlerResolverTest)}", TestAppHostOptions.Default, @out)
{
    [Fact(Timeout = 20_000)]
    public async Task BackendServerRoleShouldHandleAllEvents()
    {
        using var host = await NewAppHost(options => options with  {
            AppServicesExtender = (c, services) => {
                services
                    .AddCommandQueues(HostRole.OneBackendServer)
                    .AddFusion()
                    .AddService<ScheduledCommandTestService>();
            },
        });
        var services = host.Services;
        var eventHandlerResolver = services.GetRequiredService<EventHandlerResolver>();
        var eventHandlers = eventHandlerResolver.GetEventHandlers(HostRole.OneBackendServer);
        eventHandlers.Should().NotBeEmpty();

    }
}
