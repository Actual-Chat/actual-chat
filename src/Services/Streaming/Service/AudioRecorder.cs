using StackExchange.Redis;

namespace ActualChat.Streaming
{
    public class AudioRecorder : ServerSideRecorder<AudioRecordId, AudioRecord>, IAudioRecorder
    {
        public AudioRecorder(IConnectionMultiplexer redis) : base(redis) { }

        protected override string GetRedisKeyName(AudioRecordId recordId)
            => recordId;

        protected override string GetRedisChannelName(AudioRecordId recordId)
            => recordId.GetRedisChannelName();
    }
}
