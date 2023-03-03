using Stl.Fusion.Bridge.Interception;
using Stl.Fusion.Client.Internal;
using Stl.Fusion.Interception;

namespace ActualChat.App.Maui.Services.StartupTracing;

internal class FusionServicesWarmer
{
    private readonly IServiceProvider _services;
    private readonly ReplicaServiceProxyGenerator _proxyGenerator;
    private readonly ComputeServiceInterceptor _interceptor;

    public void ReplicaService<TClient>()
        where TClient : class
    {
        _services.GetRequiredService<ClientAccessor<TClient>>();
        _proxyGenerator.GetProxyType(typeof(TClient));
    }

    public void ComputeService<T>()
        => ComputeService(typeof(T));

    public void ComputeService(Type type)
        => _interceptor.ValidateType(type);

    public FusionServicesWarmer(IServiceProvider services)
    {
        _services = services;
        _proxyGenerator = services.GetRequiredService<ReplicaServiceProxyGenerator>();
        _interceptor = services.GetRequiredService<ComputeServiceInterceptor>();
    }
}
