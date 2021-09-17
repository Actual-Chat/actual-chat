using ActualChat.Blobs;
using ActualChat.Streaming;

namespace ActualChat.Audio
{
    public interface IAudioStreamReader : IStreamReader<BlobPart>
    { }
}