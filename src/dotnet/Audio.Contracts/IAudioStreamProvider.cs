using ActualChat.Blobs;
using ActualChat.Streaming;

namespace ActualChat.Audio
{
    public interface IAudioStreamProvider : IStreamProvider<StreamId, BlobPart>
    { }
}
