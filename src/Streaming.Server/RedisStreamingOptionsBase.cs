using System;
using StackExchange.Redis;
using StackExchange.Redis.KeyspaceIsolation;

namespace ActualChat.Streaming.Server
{
    public abstract record RedisStreamingOptionsBase<TStreamId, TPart> : RedisChannelOptions<TPart>
        where TStreamId : notnull
    {
        public string KeyPrefix { get; init; } = typeof(TPart).Name;
        public Func<TStreamId, string> StreamKeyProvider { get; init; } = streamId => $"stream:{streamId.ToString() ?? string.Empty}";
        public Func<TStreamId, string> NewPartNewsChannelKeyProvider { get; init; } = streamId => $"{typeof(TPart).Name}:{streamId}-new-part";

        public virtual IDatabase GetDatabase(IConnectionMultiplexer redis)
            => redis.GetDatabase().WithKeyPrefix(KeyPrefix);
    }
}
