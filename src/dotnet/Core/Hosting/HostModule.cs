using Microsoft.Extensions.Configuration;

namespace ActualChat.Hosting;

public interface IServerModule; // A tagging interface for server-side modules
public interface IAppModule; // A tagging interface for client-side modules

public abstract class HostModule(IServiceProvider moduleServices)
{
    private HostInfo? _hostInfo;
    private IConfiguration? _cfg;
    private ILogger? _log;

    protected IServiceProvider ModuleServices { get; } = moduleServices;
    protected HostInfo HostInfo => _hostInfo ??= ModuleServices.HostInfo();
    protected bool IsDevelopmentInstance => HostInfo.IsDevelopmentInstance;
    protected IConfiguration Cfg => _cfg ??= ModuleServices.GetRequiredService<IConfiguration>();
    protected ILogger Log => _log ??= ModuleServices.LogFor(GetType());

    protected ModuleHost Host { get; private set; } = null!;

    protected internal void Initialize(ModuleHost host)
    {
        if (this is IServerModule && !HostInfo.HostKind.IsServer())
            throw StandardError.Internal("This module can be used only on the server side.");
        if (this is IAppModule && !HostInfo.HostKind.IsApp())
            throw StandardError.Internal("This module can be used only in apps.");

        Host = host;
    }

    protected internal abstract void InjectServices(IServiceCollection services);
}

public abstract class HostModule<TSettings>(IServiceProvider moduleServices) : HostModule(moduleServices)
    where TSettings : class, new()
{
    private TSettings? _settings;

    public TSettings Settings => _settings ??= ReadSettings();

    protected virtual TSettings ReadSettings()
        => Cfg.GetSettings<TSettings>();

    protected internal override void InjectServices(IServiceCollection services)
        => services.AddSingleton(Settings);
}
