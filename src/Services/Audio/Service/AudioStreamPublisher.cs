using ActualChat.Blobs;
using ActualChat.Streaming.Server;
using StackExchange.Redis;

namespace ActualChat.Audio
{
    public class AudioStreamPublisher : StreamPublisher<BlobPart>
    {
        public AudioStreamPublisher(IConnectionMultiplexer redis, string keyPrefix) 
            : base(redis, keyPrefix)
        { }
    }
}