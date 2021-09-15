using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using Stl.Fusion.Authentication;

namespace ActualChat.Streaming
{
    public interface IUploader<in TUpload>
    {
        Task Upload(
            Session session,
            TUpload upload,
            ChannelReader<BlobPart> content,
            CancellationToken cancellationToken);
    }
}
