using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ActualChat.Events;

public static class FusionBuilderExt
{
    public static FusionBuilder AddLocalEventScheduler(this FusionBuilder builder)
    {
        var operationCompletionSinkDescriptor = new ServiceDescriptor(
            typeof(IOperationCompletionListener),
            typeof(CommandCompletionEventSink),
            ServiceLifetime.Singleton);
        if (!builder.Services.Contains(operationCompletionSinkDescriptor, new ServiceDescriptorComparer()))
            builder.Services.Add(operationCompletionSinkDescriptor);
        builder.Services.TryAddSingleton<LocalEventQueue>();
        builder.Services.TryAddSingleton<EventGateway>();
        builder.Services.AddHostedService<LocalEventScheduler>();
        return builder;
    }

    private class ServiceDescriptorComparer : IEqualityComparer<ServiceDescriptor>
    {
        public bool Equals(ServiceDescriptor? x, ServiceDescriptor? y)
        {
            if (ReferenceEquals(x, y))
                return true;

            if (x == null)
                return false;

            if (y == null)
                return false;

            return x.ServiceType == y.ServiceType
                && x.ImplementationType == y.ImplementationType
                && x.Lifetime == y.Lifetime;
        }

        public int GetHashCode(ServiceDescriptor obj)
            => obj.ServiceType.GetHashCode() ^ (397
                * obj.ImplementationType?.GetHashCode()) ?? 1
                ^ ((int)obj.Lifetime + 13);
    }
}
