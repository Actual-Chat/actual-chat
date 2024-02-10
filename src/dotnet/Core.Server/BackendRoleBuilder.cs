using System.Diagnostics.CodeAnalysis;
using ActualChat.Hosting;
using ActualLab.Rpc;

namespace ActualChat;

[StructLayout(LayoutKind.Auto)]
public readonly struct BackendRoleBuilder
{
    public FusionBuilder Fusion { get; }
    public IServiceCollection Services => Fusion.Services;
    public CommanderBuilder Commander => Fusion.Commander;
    public RpcBuilder Rpc => Fusion.Rpc;

    public HostInfo HostInfo { get; }
    public HostRole ServerRole { get; }
    public ServiceMode ServiceMode { get; }

    internal BackendRoleBuilder(
        IServiceCollection services,
        HostInfo hostInfo,
        HostRole serverRole)
    {
        Fusion = services.AddFusion(RpcServiceMode.None);
        HostInfo = hostInfo;
        ServerRole = serverRole;
        ServiceMode = hostInfo.GetServiceMode(serverRole);
    }

    // AddService auto-detects IComputeService & IRpcService

    public BackendRoleBuilder AddService<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TService,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TImplementation>()
        where TService : class, IRpcService
        where TImplementation : class, TService
        => AddService(typeof(TService), typeof(TImplementation));

    public BackendRoleBuilder AddService(
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] Type serviceType,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] Type implementationType,
        Symbol name = default)
    {
        if (!typeof(IRpcService).IsAssignableFrom(serviceType))
            throw ActualLab.Internal.Errors.MustImplement<IRpcService>(serviceType, nameof(serviceType));
        if (!serviceType.IsAssignableFrom(implementationType))
            throw ActualLab.Internal.Errors.MustBeAssignableTo(implementationType, serviceType, nameof(implementationType));

        var serverRoleServiceDef = new BackendServiceDef(serviceType, implementationType, ServerRole, ServiceMode);
        Services.Add(new ServiceDescriptor(serverRoleServiceDef.GetType(), serverRoleServiceDef));
        var isComputeService = typeof(IComputeService).IsAssignableFrom(serviceType);
        switch (ServiceMode) {
        case ServiceMode.SelfHosted:
            if (isComputeService)
                Fusion.AddService(serviceType, implementationType, RpcServiceMode.None, false);
            else
                Services.AddSingleton(serviceType, implementationType);
            break;
        case ServiceMode.Server:
            if (isComputeService)
                Fusion.AddServer(serviceType, implementationType, name, false);
            else
                Rpc.AddServer(serviceType, implementationType, name);
            break;
        case ServiceMode.Client:
            if (isComputeService)
                Fusion.AddClient(serviceType, name, false);
            else
                Rpc.AddClient(serviceType, name);
            break;
        default:
            throw StandardError.Internal("Invalid ServiceMode value.");
        }
        var isCommandService = typeof(ICommandService).IsAssignableFrom(serviceType);
        if (isCommandService)
            Commander.AddHandlers(serviceType);
        return this;
    }
}
