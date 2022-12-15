using Microsoft.Extensions.DependencyInjection.Extensions;
using Stl.Reflection;

namespace ActualChat.DependencyInjection;

public static class ServiceCollectionExt
{
    public static IServiceCollection AddServiceFactory<TService, TKey>(
        this IServiceCollection services,
        Func<IServiceProvider, TKey, TService> factory,
        ServiceLifetime lifetime = ServiceLifetime.Singleton)
    {
        var descriptor = new ServiceDescriptor(
            typeof(ServiceFactory<TService, TKey>),
            c => new ServiceFactory<TService, TKey>(c, factory),
            lifetime);
        services.Add(descriptor);
        return services;
    }

    public static IServiceCollection TryAddServiceFactory<TService, TKey>(
        this IServiceCollection services,
        Func<IServiceProvider, TKey, TService> factory,
        ServiceLifetime lifetime = ServiceLifetime.Singleton)
    {
        var descriptor = new ServiceDescriptor(
            typeof(ServiceFactory<TService, TKey>),
            c => new ServiceFactory<TService, TKey>(c, factory),
            lifetime);
        services.TryAdd(descriptor);
        return services;
    }

    public static IServiceCollection AddServiceFactory<TService, TKey>(
        this IServiceCollection services,
        ServiceLifetime lifetime = ServiceLifetime.Singleton)
        => services.AddServiceFactory<TService, TService, TKey>(lifetime);
    public static IServiceCollection AddServiceFactory<TService, TImplementation, TKey>(
        this IServiceCollection services,
        ServiceLifetime lifetime = ServiceLifetime.Singleton)
        where TImplementation : TService
    {
        var descriptor = new ServiceDescriptor(
            typeof(ServiceFactory<TService, TKey>),
            c => new ServiceFactory<TService, TKey>(c, Factory<TService, TImplementation, TKey>),
            lifetime);
        services.Add(descriptor);
        return services;
    }

    public static IServiceCollection TryAddServiceFactory<TService, TKey>(
        this IServiceCollection services,
        ServiceLifetime lifetime = ServiceLifetime.Singleton)
        => services.TryAddServiceFactory<TService, TService, TKey>(lifetime);
    public static IServiceCollection TryAddServiceFactory<TService, TImplementation, TKey>(
        this IServiceCollection services,
        ServiceLifetime lifetime = ServiceLifetime.Singleton)
        where TImplementation : TService
    {
        var descriptor = new ServiceDescriptor(
            typeof(ServiceFactory<TService, TKey>),
            c => new ServiceFactory<TService, TKey>(c, Factory<TService, TImplementation, TKey>),
            lifetime);
        services.TryAdd(descriptor);
        return services;
    }

    // Private methods

    private static TService Factory<TService, TImplementation, TKey>(
        IServiceProvider services,
        TKey key)
        where TImplementation : TService
        => (TService)typeof(TImplementation).CreateInstance(services, key);
}
