using ActualChat.Streaming.Server;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace ActualChat.Audio
{
    public class AudioRecordProducer : RedisContentProducer<AudioRecordId, AudioRecord>
    {
        public new record Options : RedisContentProducer<AudioRecordId, AudioRecord>.Options
        { }

        public AudioRecordProducer(
            Options setup,
            IConnectionMultiplexer redis,
            ILogger<AudioRecordProducer> log)
            : base(setup, redis, log)
        { }
    }
}
