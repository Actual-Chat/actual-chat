using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using MessagePack;
using Microsoft.Toolkit.HighPerformance.Buffers;
using StackExchange.Redis;
using StackExchange.Redis.KeyspaceIsolation;
using Stl.Async;

namespace ActualChat.Distribution
{
    public class ServerSideStreamingService<TMessage> : IServerSideStreamingService<TMessage>
    {
        protected readonly IConnectionMultiplexer Redis;

        public ServerSideStreamingService(IConnectionMultiplexer redis)
            => Redis = redis;

        public async Task PublishStream(string streamId, ChannelReader<TMessage> source, CancellationToken cancellationToken)
        {
            var db = GetDatabase();
            var key = new RedisKey(streamId);

            while (await source.WaitToReadAsync(cancellationToken))
            while (source.TryRead(out var message)) {
                using var bufferWriter = new ArrayPoolBufferWriter<byte>();
                MessagePackSerializer.Serialize(bufferWriter, message, MessagePackSerializerOptions.Standard, cancellationToken);
                var serialized = bufferWriter.WrittenMemory;

                await db.StreamAddAsync(key, StreamingConstants.MessageKey, serialized, maxLength: 1000, useApproximateMaxLength: true);
            }

            // TODO(AY): Should we complete w/ exceptions to mimic Channel<T> / IEnumerable<T> behavior here as well?
            await Complete(streamId, cancellationToken);
        }
        
        private async Task NotifyNewStream(IDatabase db, string streamId, CancellationToken cancellationToken)
        {
            db.ListLeftPush(StreamingConstants.Queue, streamId);
            
            var subscriber = Redis.GetSubscriber();
            await subscriber.PublishAsync(StreamingConstants.AudioRecordingQueue, string.Empty);
        }


        public async Task Publish(string streamId, TMessage message, CancellationToken cancellationToken)
        {
            var db = GetDatabase();
            var key = new RedisKey(streamId);

            using var bufferWriter = new ArrayPoolBufferWriter<byte>();
            MessagePackSerializer.Serialize(bufferWriter, message, MessagePackSerializerOptions.Standard, cancellationToken);
            var serialized = bufferWriter.WrittenMemory;

            await db.StreamAddAsync(key, StreamingConstants.MessageKey, serialized, maxLength: 1000, useApproximateMaxLength: true);
        }

        public async Task Complete(string streamId, CancellationToken cancellationToken)
        {
            var db = GetDatabase();
            var key = new RedisKey(streamId);

            await db.StreamAddAsync(key, StreamingConstants.StatusKey,  StreamingConstants.Completed, maxLength: 1000, useApproximateMaxLength: true);

            // TODO(AY): Store the key of completed stream to some persistent store & add a dedicated serv. to GC them?
            Task.Delay(TimeSpan.FromMinutes(1), default)
                .ContinueWith(_ => db.KeyDelete(key), CancellationToken.None)
                .Ignore();
        }

        // Protected methods

        protected virtual IDatabase GetDatabase()
            => Redis.GetDatabase().WithKeyPrefix(typeof(TMessage).Name);
    }
}
