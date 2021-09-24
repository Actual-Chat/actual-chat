using System;
using StackExchange.Redis;
using StackExchange.Redis.KeyspaceIsolation;

namespace ActualChat.Streaming.Server
{
    public abstract record RedisStreamingOptionsBase<TStreamId, TPart>
        where TStreamId : notnull
    {
        public string KeyPrefix { get; init; } = typeof(TPart).Name;
        public RedisStreamer<TPart>.Options PartStreamerOptions { get; init; } = new();

        public virtual IDatabase GetDatabase(IConnectionMultiplexer redis)
            => redis.GetDatabase().WithKeyPrefix(KeyPrefix);
        public virtual RedisStreamer<TPart> GetPartStreamer(IDatabase database, TStreamId streamId)
            => new(PartStreamerOptions, database, streamId.ToString() ?? "");

    }
}
