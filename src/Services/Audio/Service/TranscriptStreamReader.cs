using ActualChat.Streaming.Server;
using StackExchange.Redis;

namespace ActualChat.Audio
{
    public class TranscriptStreamReader : StreamReader<TranscriptPart>, ITranscriptStreamReader
    {
        public TranscriptStreamReader(IConnectionMultiplexer redis) : base(redis)
        { }
    }
}