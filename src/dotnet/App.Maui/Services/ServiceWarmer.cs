using Stl.Fusion.Bridge.Interception;
using Stl.Fusion.Client.Internal;
using Stl.Fusion.Interception;

namespace ActualChat.App.Maui.Services;

internal class ServiceWarmer
{
    private IServiceProvider Services { get; }

    public ServiceWarmer(IServiceProvider services)
    {
        Services = services;
        services.GetRequiredService<ComputeServiceInterceptor>();
        services.GetRequiredService<ReplicaServiceInterceptor>();
    }

    public void ReplicaService<T>()
        where T: class, IComputeService
    {
        Services.GetRequiredService<ClientAccessor<T>>();
        Services.GetRequiredService<T>();
    }

    public void ComputeService<T>()
        where T: class, IComputeService
        => Services.GetRequiredService<T>();
}
