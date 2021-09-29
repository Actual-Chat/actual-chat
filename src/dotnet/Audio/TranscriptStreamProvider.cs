using ActualChat.Streaming.Server;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace ActualChat.Audio
{
    public class TranscriptStreamProvider : RedisStreamProvider<StreamId, TranscriptPart>, ITranscriptStreamProvider
    {
        public new record Options : RedisStreamProvider<StreamId, TranscriptPart>.Options
        { }

        public TranscriptStreamProvider(
            Options setup,
            RedisDb rootRedisDb,
            ILogger<TranscriptStreamProvider> log)
            : base(setup, rootRedisDb, log)
        { }
    }
}
