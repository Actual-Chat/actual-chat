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
            public string NewContentNewsChannelKey { get; init; } = "new-content";
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
            var streamKey = Setup.StreamKeyProvider(streamId);

            var firstCycle = true;
            while (await content.WaitToReadAsync(cancellationToken)) {
                while (content.TryRead(out var message)) {
                    using var bufferWriter = new ArrayPoolBufferWriter<byte>();
                    // ReSharper disable once MethodSupportsCancellation
                    MessagePackSerializer.Serialize(bufferWriter, message);
                    var serialized = bufferWriter.WrittenMemory;

                    await db.StreamAddAsync(
                        streamKey,
                        Setup.PartKey,
                        serialized,
                        maxLength: 1000,
                        useApproximateMaxLength: true);
                }

                if (firstCycle) {
                    firstCycle = false;
                    await NotifyNewStream(db, streamKey);
                }
                await NotifyNewPart(streamId);
            }
            if (firstCycle)
                await NotifyNewStream(db, streamKey);

            // TODO(AY): Should we complete w/ exceptions to mimic Channel<T> / IEnumerable<T> behavior here as well?
            await Complete(streamId, cancellationToken);
        }

        // Protected methods

        protected async Task Complete(TStreamId streamId, CancellationToken cancellationToken)
        {
            var db = Setup.GetDatabase(Redis);
            var streamKey = Setup.StreamKeyProvider(streamId);

            await db.StreamAddAsync(
                streamKey,
                Setup.StatusKey,
                Setup.CompletedStatus,
                maxLength: 1000,
                useApproximateMaxLength: true);

            // TODO(AY): Store the key of completed stream to some persistent store & add a dedicated serv. to GC them?
            _ = Task.Delay(TimeSpan.FromMinutes(1), default)
                .ContinueWith(_ => db.KeyDelete(streamKey), CancellationToken.None);
        }

        protected async Task NotifyNewStream(IDatabase db, string streamKey)
        {
            db.ListLeftPush(Setup.NewContentNewsChannelKey, streamKey);
            var subscriber = Redis.GetSubscriber();
            await subscriber.PublishAsync(Setup.NewContentNewsChannelKey, string.Empty);
        }

        protected async Task NotifyNewPart(TStreamId streamId)
        {
            var subscriber = Redis.GetSubscriber();
            await subscriber.PublishAsync(Setup.NewPartNewsChannelKeyProvider(streamId), string.Empty);
        }
    }
}
