namespace ActualChat.ScheduledCommands;

public class ServiceDescriptorComparer : IEqualityComparer<ServiceDescriptor>
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
