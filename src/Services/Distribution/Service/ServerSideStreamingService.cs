using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using MessagePack;
using Microsoft.Toolkit.HighPerformance.Buffers;
using StackExchange.Redis;
using StackExchange.Redis.KeyspaceIsolation;

namespace ActualChat.Distribution
{
    public class ServerSideStreamingService<TMessage> : IServerSideStreamingService<TMessage>
    {
        private readonly IConnectionMultiplexer _redis;

        public ServerSideStreamingService(IConnectionMultiplexer redis)
        {
            _redis = redis;
        }

        public async Task PublishStream(string streamId, ChannelReader<TMessage> source, CancellationToken cancellationToken)
        {
            var db = _redis.GetDatabase().WithKeyPrefix(nameof(TMessage));
            var key = new RedisKey(streamId);
            
            while (await source.WaitToReadAsync(cancellationToken))
            while (source.TryRead(out var message)) {
                using var bufferWriter = new ArrayPoolBufferWriter<byte>();
                MessagePackSerializer.Serialize(bufferWriter, message, MessagePackSerializerOptions.Standard, cancellationToken);
                var serialized = bufferWriter.WrittenMemory;
                
                await db.StreamAddAsync(key, Consts.MessageKey, serialized, maxLength: 1000, useApproximateMaxLength: true);
            }

            await Complete(streamId, cancellationToken);
        }

        public async Task Publish(string streamId, TMessage message, CancellationToken cancellationToken)
        {
            var db = _redis.GetDatabase().WithKeyPrefix(nameof(TMessage));
            var key = new RedisKey(streamId);
            
            using var bufferWriter = new ArrayPoolBufferWriter<byte>();
            MessagePackSerializer.Serialize(bufferWriter, message, MessagePackSerializerOptions.Standard, cancellationToken);
            var serialized = bufferWriter.WrittenMemory;
                
            await db.StreamAddAsync(key, Consts.MessageKey, serialized, maxLength: 1000, useApproximateMaxLength: true);
        }

        public async Task Complete(string streamId, CancellationToken cancellationToken)
        {
            var db = _redis.GetDatabase().WithKeyPrefix(nameof(TMessage));
            var key = new RedisKey(streamId);
            
            await db.StreamAddAsync(key, Consts.StatusKey,  Consts.Completed, maxLength: 1000, useApproximateMaxLength: true);

            // TODO: AK - AY please review
            Task.Delay(TimeSpan.FromMinutes(1)).ContinueWith(_ => db.KeyDelete(key));
        }
    }
}