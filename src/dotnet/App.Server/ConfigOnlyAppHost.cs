namespace ActualChat.App.Server;

/// <summary>
/// Provides a minimal <see cref="AppHost"/> instance with only configuration services for early startup access.
/// </summary>
public static class ConfigOnlyAppHost
{
    private static readonly LazySlim<AppHost> InstanceLazy = new(new AppHost().Build(coreServicesOnly: true));

    public static AppHost Instance => InstanceLazy.Value;
    public static IServiceProvider Services => InstanceLazy.Value.Services;
    public static IConfiguration Configuration => InstanceLazy.Value.Services.Configuration();
}
