using ActualChat.Flows;
using ActualChat.Hosting;
using ActualChat.Testing.Host;
using ActualLab.CommandR.Configuration;
using ActualLab.Fusion;

namespace ActualChat.Core.Server.IntegrationTests.Operations;

// Nothing declares [DeferredInvalidationMode]: AppDeferredInvalidationModeResolver derives it from
// the service's ServiceMode. A single-host test can't tell the three modes apart at runtime - every
// mode ends up invalidating this process - so the resolver's answer is asserted.
[Trait("Category", "Slow")]
public sealed class DeferredInvalidationModeResolverTest(ITestOutputHelper @out)
    : AppHostTestBase($"x-{nameof(DeferredInvalidationModeResolverTest)}", TestAppHostOptions.Default, @out)
{
    [Fact(Timeout = 120_000)]
    public async Task NoBackendCommandHandlerShouldResolveToLocal()
    {
        await using var h = await NewAppHost();
        var services = h.Services;
        var resolver = services.GetRequiredService<DeferredInvalidationModeResolver>();
        var backendServiceDefs = services.GetRequiredService<BackendServiceDefs>();

        // act
        var rows = new List<(string Service, DeferredInvalidationMode Mode, ServiceMode? ServiceMode)>();
        foreach (var handler in services.GetRequiredService<CommandHandlerRegistry>().Handlers) {
            if (handler is not IMethodCommandHandler methodHandler)
                continue;

            var serviceType = methodHandler.ServiceType;
            rows.Add((serviceType.GetName(),
                resolver.Resolve(methodHandler),
                backendServiceDefs.TryGet(serviceType, out var serviceDef) ? serviceDef.ServiceMode : null));
        }

        // assert
        rows.Count.Should().BeGreaterThan(50, "the registry should hold every command handler");
        foreach (var (service, mode, serviceMode) in rows.DistinctBy(x => x.Service).OrderBy(x => x.Service))
            Out.WriteLine($"{mode,-12} {service}{(serviceMode is { } m ? $" [{m:G}]" : "")}");

        // A backend owns state other hosts cache, so a block of its own must leave this process.
        // Local here is the silent failure this test exists for: it would strand every other host.
        foreach (var row in rows.Where(x => x.ServiceMode is not null))
            row.Mode.Should().NotBe(DeferredInvalidationMode.Local,
                $"{row.Service} is a backend service, so its invalidations have to reach other hosts");

        // Only a ServiceMode.Distributed service is routed per key, so only there does invalidating
        // on the owner host reach the value
        foreach (var row in rows.Where(x => x.ServiceMode is ServiceMode.Distributed))
            row.Mode.Should().Be(DeferredInvalidationMode.Distributed,
                $"{row.Service} is routed per key, so each invalidation goes to the value's owner");

        foreach (var row in rows.Where(x => x.ServiceMode is not null and not ServiceMode.Distributed))
            row.Mode.Should().Be(DeferredInvalidationMode.Replicated,
                $"{row.Service} runs on every host, so every host has to apply its invalidations");

        foreach (var row in rows.Where(x => x.ServiceMode is null))
            row.Mode.Should().Be(DeferredInvalidationMode.Local,
                $"{row.Service} is an API service - it composes backend values rather than owning any");
    }

    [Fact(Timeout = 120_000)]
    public async Task TheDerivedModeShouldFollowServiceMode()
    {
        await using var h = await NewAppHost();
        var services = h.Services;
        var resolver = services.GetRequiredService<DeferredInvalidationModeResolver>();

        // act & assert
        // ICoachBackend and IFlowBackend are ServiceMode.Distributed, so each invalidation routes to
        // the host owning its value. IChatsBackend has a shard scheme but is ServiceMode.Local, which
        // is what makes a shard scheme the wrong thing to derive from. IChats owns nothing.
        Mode(typeof(ICoachBackend)).Should().Be(DeferredInvalidationMode.Distributed);
        Mode(typeof(IFlowBackend)).Should().Be(DeferredInvalidationMode.Distributed);
        Mode(typeof(IChatsBackend)).Should().Be(DeferredInvalidationMode.Replicated);
        Mode(typeof(IChats)).Should().Be(DeferredInvalidationMode.Local);
        return;

        DeferredInvalidationMode Mode(Type serviceType)
        {
            var handler = services.GetRequiredService<CommandHandlerRegistry>().Handlers
                .OfType<IMethodCommandHandler>()
                .First(x => x.ServiceType == serviceType);
            return resolver.Resolve(handler);
        }
    }
}
