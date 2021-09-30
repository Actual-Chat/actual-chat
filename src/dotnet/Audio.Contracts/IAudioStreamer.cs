using ActualChat.Blobs;

namespace ActualChat.Audio
{
    public interface IAudioStreamer
    {
        public Task<ChannelReader<BlobPart>> GetAudioStream(StreamId streamId, CancellationToken cancellationToken);
    }
}
