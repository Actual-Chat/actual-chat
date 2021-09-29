using StackExchange.Redis;

namespace ActualChat.Streaming.Server;

public class RedisQueue<T> : IAsyncDisposable
{
    public record Options
    {
        public string EnqueuePubSubKeySuffix { get; init; } = "-updates";
        public TimeSpan DequeueTimeout { get; init; } = TimeSpan.FromSeconds(0.250);
        public IByteSerializer<T> Serializer { get; init; } = ByteSerializer<T>.Default;
    }

    public Options Setup { get; }
    public IDatabase Database { get; }
    public string Key { get; }
    public RedisPubSub EnqueuePubSub { get; }

    public RedisQueue(Options setup, IDatabase database, string key)
    {
        Setup = setup;
        Database = database;
        Key = key;
        EnqueuePubSub = new RedisPubSub(database, $"{typeof(T).Name}-{Key}{Setup.EnqueuePubSubKeySuffix}");
    }

    public ValueTask DisposeAsync()
        => EnqueuePubSub.DisposeAsync();

    public async Task Enqueue(T item)
    {
        using var bufferWriter = Setup.Serializer.Writer.Write(item);
        await Database.ListLeftPushAsync(Key, bufferWriter.WrittenMemory);
        await EnqueuePubSub.Publish(RedisValue.EmptyString);
    }

    public async Task<T> Dequeue(CancellationToken cancellationToken = default)
    {
        while (true) {
            var value = await Database.ListRightPopAsync(Key);
            if (!value.IsNullOrEmpty)
                return Setup.Serializer.Reader.Read(value);
            await EnqueuePubSub.Fetch(cancellationToken)
                .WithTimeout(Setup.DequeueTimeout, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    public Task Remove()
        => Database.KeyDeleteAsync(Key, CommandFlags.FireAndForget);
}
