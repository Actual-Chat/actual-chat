using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using ActualChat.Blobs;
using ActualChat.Processing;
using MessagePack;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using Stl;

namespace ActualChat.Streaming.Server
{
    public abstract class RedisContentProducer<TContentId, TContent> :
            IAsyncProducer<TContent>,
            IStreamProvider<TContentId, BlobPart>,
            IAsyncDisposable
        where TContentId : notnull
        where TContent : class, IHasId<TContentId>
    {
        public record Options : RedisStreamingOptionsBase<TContentId, BlobPart>
        {
            public string ContentQueueKey { get; init; } = "content";
            public RedisQueue<TContent>.Options ContentQueueOptions { get; init; } = new();

            public Options()
                => KeyPrefix = typeof(TContent).Name;

            public virtual RedisQueue<TContent> GetContentQueue(IDatabase database)
                => new(ContentQueueOptions, database, ContentQueueKey);

        }

        protected Options Setup { get; }
        protected IConnectionMultiplexer Redis { get; }
        protected IDatabase Database { get; }
        protected RedisQueue<TContent> ContentQueue { get; }
        protected ILogger Log { get; }

        protected RedisContentProducer(
            Options setup,
            IConnectionMultiplexer redis,
            ILogger<RedisContentProducer<TContentId, TContent>> log)
        {
            Log = log;
            Setup = setup;
            Redis = redis;
            Database = Setup.GetDatabase(Redis);
            ContentQueue = Setup.GetContentQueue(Database);
        }

        public ValueTask DisposeAsync()
            => ContentQueue.DisposeAsync();

        public async ValueTask<TContent> Produce(CancellationToken cancellationToken)
            => await ContentQueue.Dequeue(cancellationToken);

        public Task<ChannelReader<BlobPart>> GetStream(TContentId streamId, CancellationToken cancellationToken)
        {
            var channel = Channel.CreateBounded<BlobPart>(
                new BoundedChannelOptions(100) {
                    FullMode = BoundedChannelFullMode.Wait,
                    SingleReader = true,
                    SingleWriter = true,
                    AllowSynchronousContinuations = true
                });
            var partStreamer = Setup.GetPartStreamer(Database, streamId);
            _ = partStreamer.Read(channel, cancellationToken);
            return Task.FromResult(channel.Reader);
        }
    }
}
