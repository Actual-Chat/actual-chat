using ActualChat.Blobs;
using ActualChat.Streaming.Server;
using StackExchange.Redis;

namespace ActualChat.Audio
{
    public class AudioStreamReader : StreamReader<BlobPart>, IAudioStreamReader
    {
        public AudioStreamReader(IConnectionMultiplexer redis) : base(redis)
        { }
    }
}