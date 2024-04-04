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
    private HostInfo? _hostInfo;
    private IConfiguration? _cfg;
    private ILogger? _log;

    public IServiceProvider ModuleServices { get; } = moduleServices;
    public HostInfo HostInfo => _hostInfo ??= ModuleServices.HostInfo();
    public IConfiguration Cfg => _cfg ??= ModuleServices.Configuration();
    public ILogger Log => _log ??= ModuleServices.LogFor(GetType());

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

public abstract class HostModule<TSettings>(IServiceProvider moduleServices) : HostModule(moduleServices)
    where TSettings : class, new()
{
    private TSettings? _settings;

#pragma warning disable CA1721
    public TSettings Settings => _settings ??= GetSettings();
#pragma warning restore CA1721

    protected virtual TSettings GetSettings()
        => Cfg.Settings<TSettings>();

    protected internal override void Initialize(ModuleHost host, IServiceCollection services)
    {
        base.Initialize(host, services);
        if (IsUsed)
            services.AddSingleton(Settings);
    }
}
