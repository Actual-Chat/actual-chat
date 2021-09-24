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
        protected IConnectionMultiplexer Redis { get; }
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

        public Task PublishStream(TStreamId streamId, ChannelReader<TPart> content, CancellationToken cancellationToken)
        {
            var db = Setup.GetDatabase(Redis);
            var streamer = Setup.GetPartStreamer(db, streamId);
            return streamer.Write(content, cancellationToken);
        }
    }
}
