using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ActualChat.DependencyInjection;

public static class ServiceCollectionExt
{
    public static IServiceCollection TryAddTypeMapper<TScope>(
        this IServiceCollection services,
        Action<Dictionary<Type, Type>>? typeMapBuilder = null)
    {
        services.TryAddSingleton(c => new TypeMapper<TScope>(c));
        services.AddSingleton(_ => new TypeMap<TScope>(typeMapBuilder));
        return services;
    }

    public static IServiceCollection AddTypeMapper<TScope>(
        this IServiceCollection services,
        Action<Dictionary<Type, Type>>? typeMapBuilder = null)
    {
        services.AddSingleton(c => new TypeMapper<TScope>(c));
        services.AddSingleton(_ => new TypeMap<TScope>(typeMapBuilder));
        return services;
    }

    public static IServiceCollection AddTypeMap<TScope>(
        this IServiceCollection services,
        Action<Dictionary<Type, Type>> typeMapBuilder)
        => services.AddSingleton(_ => new TypeMap<TScope>(typeMapBuilder));
}
