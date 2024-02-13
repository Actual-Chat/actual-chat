using System.Diagnostics.CodeAnalysis;
using ActualChat.Hosting;
using ActualLab.Rpc;

namespace ActualChat;

[StructLayout(LayoutKind.Auto)]
public readonly struct BackendServiceBuilder
{
    public FusionBuilder Fusion { get; }
    public IServiceCollection Services => Fusion.Services;
    public CommanderBuilder Commander => Fusion.Commander;
    public RpcBuilder Rpc => Fusion.Rpc;
    public HostInfo HostInfo { get; }
    public ILogger? Log { get; }

    internal BackendServiceBuilder(IServiceCollection services, HostInfo hostInfo, ILogger? log)
    {
        Fusion = services.AddFusion(RpcServiceMode.None);
        HostInfo = hostInfo;
        Log = log;
    }

    // GetServiceMode

    public ServiceMode GetServiceMode<TService>()
        => GetServiceMode(typeof(TService));
    public ServiceMode GetServiceMode(Type serviceType)
    {
        var servedByRoles = HostRoles.GetServedByRoles(serviceType);
        return HostInfo.GetServiceMode(servedByRoles);
    }

    // AddService auto-detects IComputeService & IRpcService

    public BackendServiceBuilder AddService<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TService,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TImplementation>()
        where TService : class, IRpcService
        where TImplementation : class, TService
        => AddService(typeof(TService), typeof(TImplementation));

    public BackendServiceBuilder AddService(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] Type serviceType,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] Type implementationType,
        Symbol name = default)
    {
        if (!typeof(IRpcService).IsAssignableFrom(serviceType))
            throw ActualLab.Internal.Errors.MustImplement<IRpcService>(serviceType, nameof(serviceType));
        if (!serviceType.IsAssignableFrom(implementationType))
            throw ActualLab.Internal.Errors.MustBeAssignableTo(implementationType, serviceType, nameof(implementationType));

        var servedByRoles = new ServedByRoleSet(HostRoles.GetServedByRoles(serviceType));
        var serviceMode = HostInfo.GetServiceMode(servedByRoles.AllRoles);
        var serviceDef = new BackendServiceDef(serviceType, implementationType, servedByRoles, serviceMode);
        Services.Add(new ServiceDescriptor(typeof(BackendServiceDef), serviceDef));

        var isComputeService = typeof(IComputeService).IsAssignableFrom(serviceType);
        switch (serviceMode) {
        case ServiceMode.SelfHosted:
            if (isComputeService)
                Fusion.AddService(serviceType, implementationType, RpcServiceMode.None);
            else
                Commander.AddService(serviceType, implementationType);
            break;
        case ServiceMode.Server:
            if (isComputeService)
                Fusion.AddServer(serviceType, implementationType, name);
            else {
                Rpc.AddServer(serviceType, implementationType, name);
                Commander.AddHandlers(serviceType, implementationType);
            }
            break;
        case ServiceMode.Client:
            if (isComputeService)
                Fusion.AddClient(serviceType, name);
            else {
                Rpc.AddClient(serviceType, name);
                Commander.AddHandlers(serviceType);
            }
            break;
        default:
            throw StandardError.Internal("Invalid ServiceMode value.");
        }
        return this;
    }
}
