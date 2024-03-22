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
            AppServicesExtender = (c, services) => {
                services
                    .AddNatsQueues()
                    .AddFusion()
                    .AddService<ScheduledCommandTestService>();
            },
        });
        var services = host.Services;
        var eventHandlerResolver = services.GetRequiredService<EventHandlerRegistry>();
        var eventHandlers = eventHandlerResolver.AllEventHandlers;
        eventHandlers.Should().NotBeEmpty();
    }
}
