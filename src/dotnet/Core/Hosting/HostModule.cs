using Microsoft.Extensions.Configuration;

namespace ActualChat.Hosting;

public interface IServerModule; // A tagging interface for server-side modules
public interface IAppModule; // A tagging interface for client-side modules
public interface IBlazorUIModule
{
    public static abstract string ImportName { get; }
}

public abstract class HostModule(IServiceProvider moduleServices)
{
    public IServiceProvider ModuleServices { get; } = moduleServices;
    public HostInfo HostInfo => field ??= ModuleServices.HostInfo();
    public IConfiguration Cfg => field ??= ModuleServices.Configuration();
    public ILogger Log => field ??= ModuleServices.LogFor(GetType());

    public ModuleHost Host { get; private set; } = null!;
    public bool IsUsed { get; protected set; } = true;

    protected internal virtual void Initialize(ModuleHost host, IServiceCollection services)
    {
        if (this is IServerModule && !HostInfo.HostKind.IsServer())
            throw StandardError.Internal("This module can be used only on the server side.");
        if (this is IAppModule && !HostInfo.HostKind.IsApp())
            throw StandardError.Internal("This module can be used only in apps.");
        if (this is IBlazorUIModule && !HostInfo.HasRole(HostRole.BlazorHost))
            IsUsed = false;

        Host = host;
    }

    protected internal abstract void InjectServices(IServiceCollection services);
}

public abstract class HostModule<
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TSettings>(
    IServiceProvider moduleServices
    ) : HostModule(moduleServices)
    where TSettings : class, new()
{
    public TSettings Settings => field ??= GetSettings();

    protected virtual TSettings GetSettings()
        => Cfg.Settings<TSettings>();

    protected internal override void Initialize(ModuleHost host, IServiceCollection services)
    {
        base.Initialize(host, services);
        if (IsUsed)
            services.AddSingleton(Settings);
    }
}
