using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using ActualChat.Blobs;
using ActualChat.Processing;
using ActualChat.Streaming.Server.Internal;
using MessagePack;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using Stl;
using Stl.Async;

namespace ActualChat.Streaming.Server
{
    public abstract class RedisContentProducer<TContentId, TContent> :
            IAsyncProducer<TContent>,
            IStreamProvider<TContentId, BlobPart>
        where TContentId : notnull
        where TContent : class, IHasId<TContentId>
    {
        public record Options : RedisStreamingOptionsBase<TContentId, BlobPart>
        {
            public string NewContentNewsChannelKey { get; init; } = "new-content";
            public TimeSpan WaitForNewContentTimeout { get; init; } = TimeSpan.FromSeconds(25);

            public Options() => KeyPrefix = typeof(TContent).Name;
        }

        protected Options Setup { get; init; }
        protected IConnectionMultiplexer Redis { get; init; }
        protected ILogger Log { get; }

        public RedisContentProducer(
            Options setup,
            IConnectionMultiplexer redis,
            ILogger<RedisContentProducer<TContentId, TContent>> log)
        {
            Log = log;
            Setup = setup;
            Redis = redis;
        }

        public async ValueTask<TContent> Produce(CancellationToken cancellationToken)
        {
            var db = Setup.GetDatabase(Redis);
            var subscriber = Redis.GetSubscriber();
            try {
                var newContentNews = await subscriber.SubscribeAsync(Setup.NewContentNewsChannelKey);
                while (true) {
                    await newContentNews.ReadAsync(cancellationToken).AsTask()
                        .WithTimeout(Setup.WaitForNewContentTimeout, cancellationToken);
                    var serializedContent = await db.ListRightPopAsync(Setup.NewContentNewsChannelKey);
                    if (serializedContent.IsNullOrEmpty) // Another consumer already popped the value
                        continue;
                    return MessagePackSerializer.Deserialize<TContent>(serializedContent);
                }

            }
            finally {
                try {
                    if (subscriber != null)
                        await subscriber.UnsubscribeAsync(Setup.NewContentNewsChannelKey);
                }
                catch {
                    // Intended
                }
            }
        }

        public Task<ChannelReader<BlobPart>> GetStream(TContentId streamId, CancellationToken cancellationToken)
        {
            var db = Setup.GetDatabase(Redis);
            var channel = Channel.CreateBounded<BlobPart>(
                new BoundedChannelOptions(100) {
                    FullMode = BoundedChannelFullMode.Wait,
                    SingleReader = true,
                    SingleWriter = true,
                    AllowSynchronousContinuations = true
                });

            var streamConverter = Setup.CreateStreamConverter(db, Log);
            _ = streamConverter.Convert(streamId, channel.Writer, cancellationToken);
            return Task.FromResult(channel.Reader);
        }
    }
}
