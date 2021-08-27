using StackExchange.Redis;

namespace ActualChat.Distribution
{
    public class TranscriptStreamingService : StreamingService<TranscriptMessage>
    {
        public TranscriptStreamingService(IConnectionMultiplexer redis) : base(redis)
        {
        }
    }
}