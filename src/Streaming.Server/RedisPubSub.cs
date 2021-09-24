using System;
using System.Threading;
using System.Threading.Tasks;
using ActualChat.Serialization;
using StackExchange.Redis;
using StackExchange.Redis.KeyspaceIsolation;
using Stl.Async;

namespace ActualChat.Streaming.Server
{
    public class RedisPubSub : AsyncDisposableBase
    {
        private ISubscriber? _subscriber;
        private ChannelMessageQueue? _queue;

        public IDatabase Database { get; }
        public string Key { get; }
        public string PrefixedKey { get; }
        public ISubscriber Subscriber => _subscriber ??= Database.Multiplexer.GetSubscriber();

        public RedisPubSub(IDatabase database, string key)
        {
            Database = database;
            Key = key;
            PrefixedKey = ""; // TODO(AY): make it work
        }

        protected override async ValueTask DisposeInternal(bool disposing)
        {
            var queue = _queue;
            if (queue != null)
                await queue.UnsubscribeAsync().ConfigureAwait(false);
        }

        public async ValueTask<ChannelMessageQueue> GetQueue()
            => _queue ??= await Subscriber.SubscribeAsync(Key).ConfigureAwait(false);

        public Task<long> Publish(RedisValue item)
            => Subscriber.PublishAsync(Key, item);

        public async Task<RedisValue> Fetch(CancellationToken cancellationToken = default)
        {
            var queue = await GetQueue().ConfigureAwait(false);
            var message = await queue.ReadAsync(cancellationToken).ConfigureAwait(false);
            return message.Message;
        }
    }

    public class RedisPubSub<T> : RedisPubSub
    {
        public ByteSerializer<T> Serializer { get; }

        public RedisPubSub(IDatabase database, string key, ByteSerializer<T>? serializer = null) : base(database, key)
            => Serializer = serializer ?? ByteSerializer<T>.Default;

        public Task<long> PublishRaw(RedisValue item)
            => base.Publish(item);
        public Task<RedisValue> FetchRaw(CancellationToken cancellationToken = default)
            => base.Fetch(cancellationToken);

        public async Task<long> Publish(T item)
        {
            using var writer = Serializer.Serialize(item);
            return await base.Publish(writer.WrittenMemory).ConfigureAwait(false);
        }

        public new async Task<T> Fetch(CancellationToken cancellationToken = default)
        {
            var value = await base.Fetch(cancellationToken).ConfigureAwait(false);
            return Serializer.Deserialize((ReadOnlyMemory<byte>) value);
        }
    }
}
