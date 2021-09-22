using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using MessagePack;
using Microsoft.Extensions.Logging;
using Microsoft.Toolkit.HighPerformance.Buffers;
using StackExchange.Redis;

namespace ActualChat.Streaming.Server
{
    public class RedisStreamPublisher<TStreamId, TPart> : IStreamPublisher<TStreamId, TPart>
        where TStreamId : notnull
    {
        public record Options : RedisStreamingOptionsBase<TStreamId, TPart>
        {
            public string NewStreamNewsChannelKey { get; init; } = "new-stream";
        }

        protected Options Setup { get; init; }
        protected IConnectionMultiplexer Redis { get; init; }
        protected ILogger Log { get; }

        public RedisStreamPublisher(
            Options setup,
            IConnectionMultiplexer redis,
            ILogger<RedisStreamPublisher<TStreamId, TPart>> log)
        {
            Log = log;
            Setup = setup;
            Redis = redis;
        }

        public async Task PublishStream(TStreamId streamId, ChannelReader<TPart> content, CancellationToken cancellationToken)
        {
            var db = Setup.GetDatabase(Redis);
            var key = Setup.StreamKeyProvider(streamId);

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

                    await db.StreamAddAsync(key, Setup.PartKey, serialized,
                        maxLength: 1000, useApproximateMaxLength: true);
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

        // Protected methods

        protected async Task Complete(TStreamId streamId, CancellationToken cancellationToken)
        {
            var db = Setup.GetDatabase(Redis);
            var key = Setup.StreamKeyProvider(streamId);

            await db.StreamAddAsync(key, Setup.StatusKey,  Setup.CompletedStatus,
                maxLength: 1000, useApproximateMaxLength: true);

            // TODO(AY): Store the key of completed stream to some persistent store & add a dedicated serv. to GC them?
            _ = Task.Delay(TimeSpan.FromMinutes(1), default)
                .ContinueWith(_ => db.KeyDelete(key), CancellationToken.None);
        }

        protected async Task NotifyNewStream(IDatabase db, TStreamId streamId)
        {
            db.ListLeftPush(Setup.NewStreamNewsChannelKey, Setup.NewPartNewsChannelKeyProvider(streamId));
            var subscriber = Redis.GetSubscriber();
            await subscriber.PublishAsync(Setup.NewStreamNewsChannelKey, string.Empty);
        }

        protected async Task NotifyNewMessage(TStreamId streamId)
        {
            var subscriber = Redis.GetSubscriber();
            await subscriber.PublishAsync(Setup.NewPartNewsChannelKeyProvider(streamId), string.Empty);
        }
    }
}
