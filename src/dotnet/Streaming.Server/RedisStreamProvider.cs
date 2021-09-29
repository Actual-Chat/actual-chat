using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace ActualChat.Streaming.Server
{
    public class RedisStreamProvider<TStreamId, TPart> : IStreamProvider<TStreamId, TPart>
        where TStreamId : notnull
    {
        public record Options : RedisStreamingOptionsBase<TStreamId, TPart>
        { }

        protected Options Setup { get; init; }
        protected RedisDb RootRedisDb { get; init; }
        protected RedisDb RedisDb { get; init; }
        protected ILogger Log { get; }

        public RedisStreamProvider(
            Options setup,
            RedisDb rootRedisDb,
            ILogger<RedisStreamProvider<TStreamId, TPart>> log)
        {
            Log = log;
            Setup = setup;
            RootRedisDb = rootRedisDb;
            RedisDb = RootRedisDb.WithKeyPrefix(Setup.KeyPrefix);
        }

        public Task<ChannelReader<TPart>> GetStream(TStreamId streamId, CancellationToken cancellationToken)
        {
            var streamer = Setup.GetPartStreamer(RedisDb, streamId);
            var channel = Channel.CreateBounded<TPart>(
                new BoundedChannelOptions(100) {
                    FullMode = BoundedChannelFullMode.Wait,
                    SingleReader = true,
                    SingleWriter = true,
                    AllowSynchronousContinuations = true
                });
            _ = streamer.Read(channel, cancellationToken);
            return Task.FromResult(channel.Reader);
        }
    }
}
