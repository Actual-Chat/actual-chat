using StackExchange.Redis;

namespace ActualChat.Redis;

public class RedisQueue<T> : IAsyncDisposable
{
    public record Options
    {
        public string EnqueuePubSubKeySuffix { get; init; } = "-updates";
        public TimeSpan DequeueTimeout { get; init; } = TimeSpan.FromSeconds(0.250);
        public IByteSerializer<T> Serializer { get; init; } = ByteSerializer<T>.Default;
    }

    public Options Settings { get; }
    public RedisDb RedisDb { get; }
    public string Key { get; }
    public RedisPubSub EnqueuePubSub { get; }

    public RedisQueue(RedisDb redisDb, string key, Options? settings = null)
    {
        Settings = settings ?? new();
        RedisDb = redisDb;
        Key = key;
        EnqueuePubSub = new RedisPubSub(redisDb, $"{typeof(T).Name}-{Key}{Settings.EnqueuePubSubKeySuffix}");
    }

    public ValueTask DisposeAsync()
        => EnqueuePubSub.DisposeAsync();

    public async Task Enqueue(T item)
    {
        using var bufferWriter = Settings.Serializer.Writer.Write(item);
        await RedisDb.Database.ListLeftPushAsync(Key, bufferWriter.WrittenMemory);
        await EnqueuePubSub.Publish(RedisValue.EmptyString);
    }

    public async Task<T> Dequeue(CancellationToken cancellationToken = default)
    {
        while (true) {
            var value = await RedisDb.Database.ListRightPopAsync(Key);
            if (!value.IsNullOrEmpty)
                return Settings.Serializer.Reader.Read(value);
            await EnqueuePubSub.Fetch(cancellationToken)
                .WithTimeout(Settings.DequeueTimeout, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    public Task Remove()
        => RedisDb.Database.KeyDeleteAsync(Key, CommandFlags.FireAndForget);
}
