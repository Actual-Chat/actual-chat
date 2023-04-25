using ActualChat.UI.Blazor.Services;

namespace ActualChat.UI.Blazor;

public static class ServicesCollectionExt
{
    public static IServiceCollection ConfigureUIEvents(
        this IServiceCollection services,
        Action<UIEventHub> configurator)
        => services.AddScoped(_ => configurator);

    public static IServiceCollection ConfigureAppReplicaCache(
        this IServiceCollection services,
        Action<IAppReplicaCacheConfigurator> configurator)
        => services.AddSingleton(_ => configurator);
}
