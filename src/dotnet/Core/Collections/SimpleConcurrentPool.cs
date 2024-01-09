using ActualLab.Pooling;

namespace ActualChat.Collections;

public class SimpleConcurrentPool<T>(
    Func<T> resourceFactory,
    Func<T, bool>? resourceValidator,
    int capacity)
    : IPool<T>
{
    private readonly ConcurrentQueue<T> _queue = new();
    private long _size;

    public int Capacity { get; } = capacity;

    public SimpleConcurrentPool(Func<T> resourceFactory, int capacity)
        : this(resourceFactory, null, capacity)
    { }

    public ResourceLease<T> Rent()
    {
        if (!_queue.TryDequeue(out var resource))
            return new ResourceLease<T>(resourceFactory.Invoke(), this);

        Interlocked.Decrement(ref _size);
        return new ResourceLease<T>(resource, this);
    }

    bool IResourceReleaser<T>.Release(T resource)
    {
        if (resourceValidator != null && !resourceValidator.Invoke(resource))
            return false;

        if (Interlocked.Increment(ref _size) > Capacity) {
            Interlocked.Decrement(ref _size);
            return false;
        }

        _queue.Enqueue(resource);
        return true;
    }
}
