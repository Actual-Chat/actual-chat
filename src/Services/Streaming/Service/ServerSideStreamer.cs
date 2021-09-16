using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using MessagePack;
using Microsoft.Toolkit.HighPerformance.Buffers;
using StackExchange.Redis;
using StackExchange.Redis.KeyspaceIsolation;

namespace ActualChat.Streaming
{
    public class ServerSideStreamer<TMessage> : IServerSideStreamer<TMessage>
    {
        protected IConnectionMultiplexer Redis { get; }

        public ServerSideStreamer(IConnectionMultiplexer redis)
            => Redis = redis;

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
            db.ListLeftPush(StreamingConstants.StreamQueue, (string)streamId);

            var subscriber = Redis.GetSubscriber();
            await subscriber.PublishAsync(StreamingConstants.StreamQueue, string.Empty);
        }

        private async Task NotifyNewMessage(StreamId streamId)
        {
            var subscriber = Redis.GetSubscriber();
            await subscriber.PublishAsync(streamId.GetRedisChannelName(), string.Empty);
        }

        public async Task Publish(StreamId streamId, TMessage message, CancellationToken cancellationToken)
        {
            var db = GetDatabase();
            var key = new RedisKey(streamId);

            using var bufferWriter = new ArrayPoolBufferWriter<byte>();
            MessagePackSerializer.Serialize(bufferWriter, message, MessagePackSerializerOptions.Standard, cancellationToken);
            var serialized = bufferWriter.WrittenMemory;

            await db.StreamAddAsync(key, StreamingConstants.MessageKey, serialized, maxLength: 1000, useApproximateMaxLength: true);
        }

        public async Task Complete(StreamId streamId, CancellationToken cancellationToken)
        {
            var db = GetDatabase();
            var key = new RedisKey(streamId);

            await db.StreamAddAsync(key, StreamingConstants.StatusKey,  StreamingConstants.Completed, maxLength: 1000, useApproximateMaxLength: true);

            // TODO(AY): Store the key of completed stream to some persistent store & add a dedicated serv. to GC them?
            _ = Task.Delay(TimeSpan.FromMinutes(1), default)
                .ContinueWith(_ => db.KeyDelete(key), CancellationToken.None);
        }

        // Protected methods

        protected virtual IDatabase GetDatabase()
            => Redis.GetDatabase().WithKeyPrefix(typeof(TMessage).Name);
    }
}
