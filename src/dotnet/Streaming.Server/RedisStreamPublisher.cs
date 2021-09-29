using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using Stl.Async;

namespace ActualChat.Streaming.Server
{
    public class RedisStreamPublisher<TStreamId, TPart> : IStreamPublisher<TStreamId, TPart>
        where TStreamId : notnull
    {
        public record Options : RedisStreamingOptionsBase<TStreamId, TPart>
        { }

        protected Options Setup { get; }
        protected RedisDb RootRedisDb { get; }
        protected RedisDb RedisDb { get; }
        protected ILogger Log { get; }

        public RedisStreamPublisher(
            Options setup,
            RedisDb rootRedisDb,
            ILogger<RedisStreamPublisher<TStreamId, TPart>> log)
        {
            Log = log;
            Setup = setup;
            RootRedisDb = rootRedisDb;
            RedisDb = RootRedisDb.WithKeyPrefix(Setup.KeyPrefix);
        }

        public Task PublishStream(TStreamId streamId, ChannelReader<TPart> content, CancellationToken cancellationToken)
        {
            var streamer = Setup.GetPartStreamer(RedisDb, streamId);
            return streamer.Write(content, cancellationToken);
        }
    }
}
