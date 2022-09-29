namespace ActualChat.UI.Blazor;

public static class ServicesCollectionExt
{
    public static IServiceCollection ConfigureUILifetimeEvents(this IServiceCollection services, Action<UILifetimeEvents> configurator)
        => services.AddScoped(_ => configurator);
}
