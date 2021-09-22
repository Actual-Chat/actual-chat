using System;
using StackExchange.Redis;
using StackExchange.Redis.KeyspaceIsolation;

namespace ActualChat.Streaming.Server
{
    public abstract record RedisStreamingOptionsBase<TStreamId, TPart>
        where TStreamId : notnull
    {
        public string KeyPrefix { get; init; } = typeof(TPart).Name;
        public string StatusKey { get; init; } = "s";
        public string PartKey { get; init; } = "m";
        public string CompletedStatus { get; init; } = "completed";
        public TimeSpan WaitForNewMessageTimeout { get; init; } = TimeSpan.FromSeconds(0.25);

        public Func<TStreamId, string> StreamKeyProvider { get; init; } = streamId => streamId.ToString() ?? "";
        public Func<TStreamId, string> NewPartNewsChannelKeyProvider { get; init; } = streamId => $"{streamId}-new-part";

        public virtual IDatabase GetDatabase(IConnectionMultiplexer redis)
            => redis.GetDatabase().WithKeyPrefix(KeyPrefix);
    }
}
