using Microsoft.Extensions.Configuration;

namespace ActualChat.Hosting;

public abstract class HostModule(IServiceProvider moduleServices)
{
    private HostInfo? _hostInfo;
    private IConfiguration? _cfg;
    private ILogger? _log;

    protected IServiceProvider ModuleServices { get; } = moduleServices;
    protected HostInfo HostInfo => _hostInfo ??= ModuleServices.GetRequiredService<HostInfo>();
    protected bool IsDevelopmentInstance => HostInfo.IsDevelopmentInstance;
    protected IConfiguration Cfg => _cfg ??= ModuleServices.GetRequiredService<IConfiguration>();
    protected ILogger Log => _log ??= ModuleServices.LogFor(GetType());

    protected ModuleHost Host { get; private set; } = null!;

    protected internal void Initialize(ModuleHost host)
        => Host = host;

    protected internal abstract void InjectServices(IServiceCollection services);
}

public abstract class HostModule<TSettings> : HostModule
    where TSettings : class, new()
{
    private TSettings? _settings;

    public TSettings Settings => _settings ??= ReadSettings();

    protected HostModule(IServiceProvider moduleServices) : base(moduleServices) { }

    protected virtual TSettings ReadSettings()
        => Cfg.GetSettings<TSettings>();

    protected internal override void InjectServices(IServiceCollection services)
        => services.AddSingleton(Settings);
}
