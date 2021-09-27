using ActualChat.Blobs;
using ActualChat.Streaming.Server;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace ActualChat.Audio
{
    public class AudioStreamPublisher : RedisStreamPublisher<StreamId, BlobPart>
    {
        public new record Options : RedisStreamPublisher<StreamId, BlobPart>.Options
        { }

        public AudioStreamPublisher(
            Options setup,
            IConnectionMultiplexer redis,
            ILogger<AudioStreamPublisher> log)
            : base(setup, redis, log)
        { }
    }
}
