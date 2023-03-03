using Microsoft.Extensions.Configuration;
using Stl.Plugins;

namespace ActualChat.Hosting;

public abstract class HostModule : Plugin
{
    protected HostInfo HostInfo { get; } = null!;
    protected bool IsDevelopmentInstance => HostInfo.IsDevelopmentInstance;

    protected HostModule(IPluginInfoProvider.Query _) : base(_) { }

    protected HostModule(IPluginHost plugins) : base(plugins)
        => HostInfo = plugins.GetRequiredService<HostInfo>();

    public abstract void InjectServices(IServiceCollection services);
}

public abstract class HostModule<TSettings> : HostModule
    where TSettings : class, new()
{
    public TSettings Settings { get; } = null!;

    protected HostModule(IPluginInfoProvider.Query _) : base(_) { }

    protected HostModule(IPluginHost plugins) : base(plugins)
    {
        var cfg = plugins.GetRequiredService<IConfiguration>();
        Settings = cfg.GetSettings<TSettings>();
    }

    public override void InjectServices(IServiceCollection services)
        => services.AddSingleton(Settings);
}
