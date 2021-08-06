using System.Threading;
using System.Threading.Tasks;
using Stl.Fusion;
using Stl.Fusion.Authentication;
using Stl.Serialization;

namespace ActualChat.Blobs
{
    public interface IBlobReader
    {
        [ComputeMethod]
        Task<BlobInfo?> GetInfo(Session session, string blobId, CancellationToken cancellationToken = default);
        [ComputeMethod]
        Task<Base64Encoded> ReadTail(Session session, string blobId, long maxLength, CancellationToken cancellationToken = default);
        Task<Base64Encoded> Read(Session session, string blobId, long start, long maxLength, CancellationToken cancellationToken = default);
    }
}
