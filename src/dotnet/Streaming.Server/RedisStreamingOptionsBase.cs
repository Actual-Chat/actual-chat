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

        public virtual RedisStreamer<TPart> GetPartStreamer(RedisDb redisDb, TStreamId streamId)
            => new(PartStreamerOptions, redisDb, streamId.ToString() ?? "");

    }
}
