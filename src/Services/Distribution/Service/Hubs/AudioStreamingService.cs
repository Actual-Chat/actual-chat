using StackExchange.Redis;

namespace ActualChat.Distribution
{
    public class AudioStreamingService : StreamingService<AudioMessage>
    {
        public AudioStreamingService(IConnectionMultiplexer redis) : base(redis)
        { }
    }
}