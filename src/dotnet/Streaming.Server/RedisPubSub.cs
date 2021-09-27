using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using ActualChat.Serialization;
using StackExchange.Redis;
using Stl;
using Stl.Async;

namespace ActualChat.Streaming.Server
{
    public class RedisPubSub : AsyncDisposableBase
    {
        private ISubscriber? _subscriber;
        private ChannelMessageQueue? _queue;

        public IDatabase Database { get; }
        public string Key { get; }
        public ISubscriber Subscriber => _subscriber ??= Database.Multiplexer.GetSubscriber();

        public RedisPubSub(IDatabase database, string key)
        {
            Database = database;
            Key = key;
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
        {
            if (item == RedisValue.Null)
                throw new ArgumentOutOfRangeException(
                    $"RedisValue.Null is not supported argument `{nameof(item)}` value");
            
            return Subscriber.PublishAsync(Key, item);
        }

        public async Task<RedisValue> Fetch(CancellationToken cancellationToken = default)
        {
            try {
                var queue = await GetQueue().ConfigureAwait(false);
                var message = await queue.ReadAsync(cancellationToken).ConfigureAwait(false);
                return message.Message;
            }
            catch (ChannelClosedException) { }
            return RedisValue.Null;
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

        public new async Task<T?> Fetch(CancellationToken cancellationToken = default)
        {
            var value = await base.Fetch(cancellationToken).ConfigureAwait(false);
            return value.IsNull ? default : Serializer.Deserialize(value);
        }
    }
}
