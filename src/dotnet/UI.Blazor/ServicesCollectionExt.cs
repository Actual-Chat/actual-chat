namespace ActualChat.UI.Blazor;

public static class ServicesCollectionExt
{
    public static IServiceCollection ConfigureUIEvents(
        this IServiceCollection services,
        Action<UIEventHub> configurator)
        => services.AddScoped(_ => configurator);
}
