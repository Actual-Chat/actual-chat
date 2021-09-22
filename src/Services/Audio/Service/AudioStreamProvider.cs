using ActualChat.Blobs;
using ActualChat.Streaming.Server;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace ActualChat.Audio
{
    public class AudioStreamProvider : RedisStreamProvider<StreamId, BlobPart>, IAudioStreamProvider
    {
        public new record Options : RedisStreamProvider<StreamId, BlobPart>.Options
        { }

        public AudioStreamProvider(
            Options setup,
            IConnectionMultiplexer redis,
            ILogger<AudioStreamProvider> log)
            : base(setup, redis, log)
        { }
    }
}
