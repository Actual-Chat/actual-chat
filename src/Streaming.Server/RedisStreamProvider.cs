using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using ActualChat.Streaming.Server.Internal;
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
        protected IConnectionMultiplexer Redis { get; init; }
        protected ILogger Log { get; }

        public RedisStreamProvider(
            Options setup,
            IConnectionMultiplexer redis,
            ILogger<RedisStreamProvider<TStreamId, TPart>> log)
        {
            Log = log;
            Setup = setup;
            Redis = redis;
        }

        public Task<ChannelReader<TPart>> GetStream(TStreamId streamId, CancellationToken cancellationToken)
        {
            var db = Setup.GetDatabase(Redis);
            var channel = Channel.CreateBounded<TPart>(
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
