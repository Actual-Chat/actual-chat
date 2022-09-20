using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ActualChat.UI.Blazor;

public static class ServicesCollectionExt
{
    public static IServiceCollection RegisterNavbarWidget<TComponent>(this IServiceCollection services, double order = 0, string? navbarGroupId = null)
        where TComponent : IComponent
        => RegisterNavbarWidget(services, typeof(TComponent), order, navbarGroupId);

    public static IServiceCollection RegisterNavbarWidget(this IServiceCollection services, Type componentType, double order = 0, string? navbarGroupId = null)
        => services.AddScoped(_ => new NavbarWidget(componentType) { Order = order, NavbarGroupId = navbarGroupId });

    public static IServiceCollection ConfigureUILifetimeEvents(this IServiceCollection services, Action<UILifetimeEvents> configurator)
        => services.AddScoped(_ => configurator);
}
