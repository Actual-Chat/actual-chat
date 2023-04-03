using ActualChat.UI.Blazor.Services;

namespace ActualChat.UI.Blazor;

public static class ServicesCollectionExt
{
    public static IServiceCollection ConfigureUILifetimeEvents(
        this IServiceCollection services,
        Action<UILifetimeEvents> configurator)
        => services.AddScoped(_ => configurator);

    public static IServiceCollection ConfigureAppReplicaCache(
        this IServiceCollection services,
        Action<IAppReplicaCacheConfigurator> configurator)
        => services.AddSingleton(_ => configurator);
}
