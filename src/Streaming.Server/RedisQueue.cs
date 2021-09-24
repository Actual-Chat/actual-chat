using System;
using System.Threading;
using System.Threading.Tasks;
using ActualChat.Serialization;
using StackExchange.Redis;
using Stl.Async;

namespace ActualChat.Streaming.Server
{
    public class RedisQueue<T> : IAsyncDisposable
    {
        public record Options
        {
            public string EnqueuePubSubKeySuffix { get; init; } = "-updates";
            public TimeSpan DequeueTimeout { get; init; } = TimeSpan.FromSeconds(0.25);
            public ByteSerializer<T> Serializer { get; init; } = ByteSerializer<T>.Default;
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
            EnqueuePubSub = new RedisPubSub(database, Key + Setup.EnqueuePubSubKeySuffix);
        }

        public ValueTask DisposeAsync()
            => EnqueuePubSub.DisposeAsync();

        public async Task Enqueue(T item)
        {
            using var writer = Setup.Serializer.Serialize(item);
            await Database.ListLeftPushAsync(Key, writer.WrittenMemory);
            await EnqueuePubSub.Publish(RedisValue.Null);
        }

        public async Task<T> Dequeue(CancellationToken cancellationToken = default)
        {
            while (true) {
                var value = await Database.ListRightPopAsync(Key);
                if (!value.IsNullOrEmpty)
                    return Setup.Serializer.Deserialize(value);
                var dequeueOpt = await EnqueuePubSub.Fetch(cancellationToken)
                    .WithTimeout(Setup.DequeueTimeout, cancellationToken)
                    .ConfigureAwait(false);
                if (dequeueOpt.IsNone())
                    throw new TimeoutException("Timeout while trying to dequeue an item.");
            }
        }

        public Task Remove()
            => Database.KeyDeleteAsync(Key, CommandFlags.FireAndForget);
    }
}
