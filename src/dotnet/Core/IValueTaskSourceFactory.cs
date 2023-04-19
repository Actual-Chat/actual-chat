using Microsoft.Extensions.ObjectPool;

namespace ActualChat;

/// <example>
/// <code>
///    var vts = factory.Create();
///    channel.Writer.TryWrite(vts); // on channel loop: vts.TrySetResult(...);
///    // and return value task object created like this:
///    return new ValueTask<int>(vts, vts.Version);
/// </code>
/// </example>
public interface IValueTaskSourceFactory<T>
{
    ValueTaskSource<T> Create();
    /// <summary>
    /// You shouldn't call this by yourself,
    /// it's called automatically after <c>await</c>.
    /// </summary>
    void Return(ValueTaskSource<T> valueTaskSource);
}

/// <inheritdoc cref="IValueTaskSourceFactory{T}"/>
public class PooledValueTaskSourceFactory<T> : IValueTaskSourceFactory<T>
{
    private readonly ObjectPool<ValueTaskSource<T>> _pool;

    public PooledValueTaskSourceFactory(ObjectPoolProvider poolProvider)
        => _pool = poolProvider.Create(new DefaultPooledObjectPolicy<ValueTaskSource<T>>());

    /// <inheritdoc />
    public ValueTaskSource<T> Create()
    {
        var vts = _pool.Get();
        vts.Factory = this;
        vts.Reset();
        return vts;
    }

    /// <inheritdoc />
    void IValueTaskSourceFactory<T>.Return(ValueTaskSource<T> valueTaskSource) => _pool.Return(valueTaskSource);
}
