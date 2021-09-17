using ActualChat.Streaming.Server;
using StackExchange.Redis;

namespace ActualChat.Audio
{
    public class AudioRecordReader : RecordReader<AudioRecordId, AudioRecord>
    {
        public AudioRecordReader(IConnectionMultiplexer redis) : base(redis, "audio") { }

        protected override string GetRedisKeyName(AudioRecordId recordId)
            => recordId;

        protected override string GetRedisChannelName(AudioRecordId recordId)
            => recordId.GetRedisChannelName();
    }
}
