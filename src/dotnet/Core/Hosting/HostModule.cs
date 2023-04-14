using Microsoft.Extensions.Configuration;

namespace ActualChat.Hosting;

public abstract class HostModule
{
    protected HostInfo HostInfo { get; }
    protected bool IsDevelopmentInstance => HostInfo.IsDevelopmentInstance;
    protected ModuleHost Host { get; private set; } = null!;

    protected HostModule(IServiceProvider services)
        => HostInfo = services.GetRequiredService<HostInfo>();

    protected internal void Initialize(ModuleHost host)
        => Host = host;

    protected internal abstract void InjectServices(IServiceCollection services);
}

public abstract class HostModule<TSettings> : HostModule
    where TSettings : class, new()
{
    public TSettings Settings { get; }

    protected HostModule(IServiceProvider services) : base(services)
    {
        var cfg = services.GetRequiredService<IConfiguration>();
        Settings = cfg.GetSettings<TSettings>();
    }

    protected internal override void InjectServices(IServiceCollection services)
        => services.AddSingleton(Settings);
}
