namespace ActualChat.UI.Blazor;

public static class ServicesCollectionExt
{
    public static IServiceCollection RegisterNavItems<TComponent>(this IServiceCollection services, int order = 0)
        where TComponent : IComponent
    {
        return RegisterNavItems(services, typeof(TComponent), order);
    }

    public static IServiceCollection RegisterNavItems(this IServiceCollection services, Type componentType, int order = 0)
    {
        services.AddScoped(_ => new NavBarItem(componentType) { Order = order });
        return services;
    }
}
