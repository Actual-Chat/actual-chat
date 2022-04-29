namespace ActualChat.UI.Blazor;

public static class ServicesCollectionExt
{
    public static IServiceCollection RegisterNavbarWidget<TComponent>(this IServiceCollection services, double order = 0)
        where TComponent : IComponent
        => RegisterNavbarWidget(services, typeof(TComponent), order);

    public static IServiceCollection RegisterNavbarWidget(this IServiceCollection services, Type componentType, double order = 0)
    {
        services.AddScoped(_ => new NavbarWidget(componentType) { Order = order });
        return services;
    }
}
