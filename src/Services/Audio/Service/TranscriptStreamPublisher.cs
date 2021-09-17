using ActualChat.Streaming.Server;
using StackExchange.Redis;

namespace ActualChat.Audio
{
    public class TranscriptStreamPublisher : StreamPublisher<TranscriptPart>
    {
        public TranscriptStreamPublisher(IConnectionMultiplexer redis, string keyPrefix) 
            : base(redis, keyPrefix)
        { }
    }
}