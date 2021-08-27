using StackExchange.Redis;

namespace ActualChat.Distribution
{
    public class VideoStreamingService : StreamingService<VideoMessage>
    {
        public VideoStreamingService(IConnectionMultiplexer redis) : base(redis)
        {
        }
    }
}