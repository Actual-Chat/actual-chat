using ActualChat.Streaming.Server;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace ActualChat.Audio
{
    public class TranscriptStreamPublisher : RedisStreamPublisher<StreamId, TranscriptPart>
    {
        public new record Options : RedisStreamPublisher<StreamId, TranscriptPart>.Options
        { }

        public TranscriptStreamPublisher(
            Options setup,
            IConnectionMultiplexer redis,
            ILogger<RedisStreamPublisher<StreamId, TranscriptPart>> log)
            : base(setup, redis, log)
        { }
    }
}
