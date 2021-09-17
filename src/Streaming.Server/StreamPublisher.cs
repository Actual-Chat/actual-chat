using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using MessagePack;
using Microsoft.Toolkit.HighPerformance.Buffers;
using StackExchange.Redis;
using StackExchange.Redis.KeyspaceIsolation;

namespace ActualChat.Streaming.Server
{
    public class StreamPublisher<TMessage> : IStreamPublisher<TMessage>
    {
        protected IConnectionMultiplexer Redis { get; init; }
        protected string KeyPrefix { get; init; }
        protected string QueueChannelKey { get; init; } = "queue";

        public StreamPublisher(IConnectionMultiplexer redis, string keyPrefix)
        {
            Redis = redis;
            KeyPrefix = keyPrefix;
        }

        public async Task PublishStream(StreamId streamId, ChannelReader<TMessage> content, CancellationToken cancellationToken)
        {
            var db = GetDatabase();
            var key = new RedisKey(streamId);

            var firstCycle = true;
            while (await content.WaitToReadAsync(cancellationToken)) {
                while (content.TryRead(out var message)) {
                    using var bufferWriter = new ArrayPoolBufferWriter<byte>();
                    MessagePackSerializer.Serialize(
                        bufferWriter,
                        message,
                        MessagePackSerializerOptions.Standard,
                        cancellationToken);
                    var serialized = bufferWriter.WrittenMemory;

                    await db.StreamAddAsync(
                        key,
                        StreamingConstants.MessageKey,
                      serialized,
                        maxLength: 1000,
                        useApproximateMaxLength: true);
                }

                if (firstCycle) {
                    firstCycle = false;
                    _ = NotifyNewStream(db, streamId);
                }
                _ = NotifyNewMessage(streamId);
            }
            if (firstCycle) _ = NotifyNewStream(db, streamId);

            // TODO(AY): Should we complete w/ exceptions to mimic Channel<T> / IEnumerable<T> behavior here as well?
            await Complete(streamId, cancellationToken);
        }

        private async Task NotifyNewStream(IDatabase db, StreamId streamId)
        {
            db.ListLeftPush(QueueChannelKey, (string)streamId);

            var subscriber = Redis.GetSubscriber();
            await subscriber.PublishAsync(QueueChannelKey, string.Empty);
        }

        private async Task NotifyNewMessage(StreamId streamId)
        {
            var subscriber = Redis.GetSubscriber();
            await subscriber.PublishAsync(streamId.GetRedisChannelName(), string.Empty);
        }

        public async Task Complete(StreamId streamId, CancellationToken cancellationToken)
        {
            var db = GetDatabase();
            var key = new RedisKey(streamId);

            await db.StreamAddAsync(key, StreamingConstants.StatusKey,  StreamingConstants.CompletedStatus, maxLength: 1000, useApproximateMaxLength: true);

            // TODO(AY): Store the key of completed stream to some persistent store & add a dedicated serv. to GC them?
            _ = Task.Delay(TimeSpan.FromMinutes(1), default)
                .ContinueWith(_ => db.KeyDelete(key), CancellationToken.None);
        }

        // Protected methods

        protected virtual IDatabase GetDatabase()
            => Redis.GetDatabase().WithKeyPrefix(typeof(TMessage).Name);
    }
}
