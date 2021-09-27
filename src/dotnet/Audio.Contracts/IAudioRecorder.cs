using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using ActualChat.Blobs;
using Stl.Fusion.Authentication;

namespace ActualChat.Audio
{
    public interface IAudioRecorder
    {
        Task Record(
            Session session,
            AudioRecord record,
            ChannelReader<BlobPart> content,
            CancellationToken cancellationToken);
    }
}
