using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Stl.DependencyInjection;
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
    where TSettings : class
{
    public TSettings Settings { get; } = null!;

    protected HostModule(IPluginInfoProvider.Query _) : base(_) { }

    protected HostModule(IPluginHost plugins) : base(plugins)
    {
        var settingsType = typeof(TSettings);
        var sectionName = settingsType.Name;
        var settings = (TSettings)plugins.Activate(settingsType);
        var cfg = plugins.GetRequiredService<IConfiguration>();
        cfg.GetSection(sectionName)?.Bind(settings);
        Settings = settings;
    }

    public override void InjectServices(IServiceCollection services)
    {
        services.AddSingleton(Settings);
    }
}
