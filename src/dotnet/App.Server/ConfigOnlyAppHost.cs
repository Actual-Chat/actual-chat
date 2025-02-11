namespace ActualChat.App.Server;

public static class ConfigOnlyAppHost
{
    private static readonly LazySlim<AppHost> InstanceLazy = new(new AppHost().Build(coreServicesOnly: true));

    public static AppHost Instance => InstanceLazy.Value;
    public static IServiceProvider Services => InstanceLazy.Value.Services;
    public static IConfiguration Configuration => InstanceLazy.Value.Services.Configuration();
}
