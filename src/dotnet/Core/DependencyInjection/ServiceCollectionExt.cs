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

    public static ServiceDescriptor ChangeLifetime(
        this ServiceDescriptor serviceDescriptor,
        ServiceLifetime serviceLifetime)
    {
        if (serviceDescriptor.ImplementationFactory != null)
            return new ServiceDescriptor(serviceDescriptor.ServiceType,
                serviceDescriptor.ImplementationFactory,
                serviceLifetime);

        if (serviceDescriptor.ImplementationType != null)
            return new ServiceDescriptor(serviceDescriptor.ServiceType,
                serviceDescriptor.ImplementationType,
                serviceLifetime);

        return new ServiceDescriptor(serviceDescriptor.ServiceType,
            serviceDescriptor.ImplementationInstance!);
    }

    public static IServiceCollection Replace(
        this IServiceCollection collection,
        Type serviceType,
        Func<ServiceDescriptor, ServiceDescriptor> converter)
    {
        // Remove existing
        int count = collection.Count;
        ServiceDescriptor? serviceDescriptor = null;
        for (int i = 0; i < count; i++) {
            if (collection[i].ServiceType == serviceType) {
                serviceDescriptor = collection[i];
                collection.RemoveAt(i);
                break;
            }
        }

        if (serviceDescriptor == null)
            throw StandardError.Constraint($"Service descriptor for type '{serviceType}' is not found");
        collection.Add(converter(serviceDescriptor));
        return collection;
    }

    public static IServiceCollection ReplaceAll(
        this IServiceCollection collection,
        Type serviceType,
        Func<ServiceDescriptor, ServiceDescriptor> converter)
    {
        // Remove existing
        int count = collection.Count;
        List<int>? indexesToReplace = null;
        for (int i = 0; i < count; i++) {
            if (collection[i].ServiceType == serviceType) {
                indexesToReplace ??= new List<int>();
                indexesToReplace.Add(i);
            }
        }

        if (indexesToReplace == null)
            return collection;

        var serviceDescriptors = new ServiceDescriptor[indexesToReplace.Count];
        for (int i = indexesToReplace.Count - 1; i >= 0; i--) {
            var index = indexesToReplace[i];
            var serviceDescriptor = collection[index];
            serviceDescriptors[i] = serviceDescriptor;
            collection.RemoveAt(index);
        }

        foreach (var serviceDescriptor in serviceDescriptors)
            collection.Add(converter(serviceDescriptor));

        return collection;
    }
}
