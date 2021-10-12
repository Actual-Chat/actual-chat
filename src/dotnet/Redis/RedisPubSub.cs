using StackExchange.Redis;
using Stl.Locking;

namespace ActualChat.Redis;

public class RedisPubSub : AsyncDisposableBase
{
    private readonly IAsyncLock _asyncLock = new AsyncLock(ReentryMode.UncheckedDeadlock);
    private ISubscriber? _subscriber;
    private ChannelMessageQueue? _queue;

    public RedisDb RedisDb { get; }
    public string Key { get; }
    public string FullKey { get; }
    public ISubscriber Subscriber => _subscriber ??= RedisDb.Redis.GetSubscriber();

    public RedisPubSub(RedisDb redisDb, string key)
    {
        RedisDb = redisDb;
        Key = key;
        FullKey = RedisDb.FullKey(Key);
    }

    protected override async ValueTask DisposeAsyncCore()
    {
        var queue = _queue;
        _queue = null;
        if (queue != null)
            await queue.UnsubscribeAsync().ConfigureAwait(false);
    }

    protected override void Dispose(bool disposing)
    {
        _queue?.Unsubscribe();
        _queue = null;
    }

    public async ValueTask<ChannelMessageQueue> GetQueue()
    {
        if (_queue != null)
            return _queue;
        using var _ = await _asyncLock.Lock();
        return _queue ??= await Subscriber.SubscribeAsync(FullKey).ConfigureAwait(false);
    }

    public Task<long> Publish(RedisValue item)
    {
        if (item == RedisValue.Null)
            throw new ArgumentOutOfRangeException(nameof(item), "RedisValue.Null is not supported item value.");
        return Subscriber.PublishAsync(FullKey, item);
    }

    public async Task<RedisValue> Fetch(CancellationToken cancellationToken = default)
    {
        try {
            var queue = await GetQueue().ConfigureAwait(false);
            var message = await queue.ReadAsync(cancellationToken).ConfigureAwait(false);
            return message.Message;
        }
        catch (ChannelClosedException) {
            return RedisValue.Null;
        }
    }
}

public class RedisPubSub<T> : RedisPubSub
{
    public IByteSerializer<T> Serializer { get; }

    public RedisPubSub(RedisDb redisDb, string key, ByteSerializer<T>? serializer = null)
        : base(redisDb, key)
        => Serializer = serializer ?? ByteSerializer<T>.Default;

    public Task<long> PublishRaw(RedisValue item)
        => base.Publish(item);
    public Task<RedisValue> FetchRaw(CancellationToken cancellationToken = default)
        => base.Fetch(cancellationToken);

    public async Task<long> Publish(T item)
    {
        using var bufferWriter = Serializer.Writer.Write(item);
        return await base.Publish(bufferWriter.WrittenMemory).ConfigureAwait(false);
    }

    public new async Task<Option<T>> Fetch(CancellationToken cancellationToken = default)
    {
        var value = await base.Fetch(cancellationToken).ConfigureAwait(false);
        return value.IsNull ? Option<T>.None : Serializer.Reader.Read(value);
    }
}
