using ActualLab.CommandR.Operations;

namespace ActualChat.Operations;

// Why each mode: only a ServiceMode.Distributed backend is actually routed per key, so only there
// does invalidating on the owner host reach the value - and its per-invalidation cost stays flat as
// hosts are added. Any other backend runs on every host, all of which cache its values, so all must
// be told. An API service owns nothing - its values are composed from backend ones and invalidated
// through that dependency, so its blocks stay local.
// A shard scheme is not the signal: most backends have one and are still ServiceMode.Local.

/// <summary>
/// Derives a command handler's <see cref="DeferredInvalidationMode"/> from how its service is
/// hosted, so nothing has to declare one. An explicit attribute still wins.
/// </summary>
public sealed class AppDeferredInvalidationModeResolver(
    IServiceProvider services,
    ServiceTypeResolver serviceTypeResolver
    ) : DeferredInvalidationModeResolver(serviceTypeResolver)
{
    private IServiceProvider Services { get; } = services;

    // Lazy: this resolver is registered while BackendServiceDef-s are still being collected
    private BackendServiceDefs BackendServiceDefs
        => field ??= Services.GetRequiredService<BackendServiceDefs>();

    public override DeferredInvalidationMode Resolve(IMethodCommandHandler handler)
    {
        // A handler declared on an interface runs the implementation's method, and either type may
        // be the one BackendServiceDefs knows
        var implementationType = ServiceTypeResolver.TryResolveImplementationType(handler.ServiceType);
        if (DeferredInvalidationModeAttribute.Get(handler.Method, implementationType) is { } attribute)
            return attribute.Mode;

        return TryGetServiceDef(handler.ServiceType, implementationType) is { } serviceDef
            ? serviceDef.ServiceMode is ServiceMode.Distributed
                ? DeferredInvalidationMode.Distributed
                : DeferredInvalidationMode.Replicated
            : DeferredInvalidationMode.Local;
    }

    // Private methods

    private BackendServiceDef? TryGetServiceDef(Type serviceType, Type? implementationType)
    {
        var serviceDefs = BackendServiceDefs;
        if (serviceDefs.TryGet(serviceType, out var serviceDef))
            return serviceDef;

        return implementationType is not null && serviceDefs.TryGet(implementationType, out serviceDef)
            ? serviceDef
            : null;
    }
}
