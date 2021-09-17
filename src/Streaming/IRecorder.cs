using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using ActualChat.Blobs;
using Stl.Fusion.Authentication;

namespace ActualChat.Streaming
{
    public interface IRecorder<in TRecord>
    {
        Task Record(
            Session session,
            TRecord record,
            ChannelReader<BlobPart> content,
            CancellationToken cancellationToken);
    }
}
