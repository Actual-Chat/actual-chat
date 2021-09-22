using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace ActualChat.Streaming.Server.Internal
{
    public static class RedisStreamingOptionsExt
    {
        public static RedisStreamConverter<TStreamId, TPart> CreateStreamConverter<TStreamId, TPart>(
            this RedisStreamingOptionsBase<TStreamId, TPart> options,
            IDatabase database,
            ILogger log)
            where TStreamId: notnull
            => new RedisStreamConverter<TStreamId, TPart>(options, database, log);
    }
}
