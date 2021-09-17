using ActualChat.Streaming.Server;
using StackExchange.Redis;

namespace ActualChat.Audio
{
    public class AudioRecorder : ServerSideRecorder<AudioRecordId, AudioRecord>, IAudioRecorder
    {
        public AudioRecorder(IConnectionMultiplexer redis) : base(redis, "audio") { }

        protected override string GetRedisKeyName(AudioRecordId recordId)
            => recordId;

        protected override string GetRedisChannelName(AudioRecordId recordId)
            => recordId.GetRedisChannelName();
    }
}
