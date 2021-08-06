using System.Threading;
using System.Threading.Tasks;
using RestEase;
using Stl.Fusion.Authentication;
using Stl.Fusion.Client;
using Stl.Serialization;

namespace ActualChat.Blobs.Client
{
    [RegisterRestEaseReplicaService(typeof(IBlobReader))]
    [BasePath("blobs")]
    public interface IBlobReaderDef
    {
        [Get(nameof(GetInfo))]
        Task<BlobInfo?> GetInfo(Session session, string blobId, CancellationToken cancellationToken = default);
        [Get(nameof(ReadTail))]
        Task<Base64Encoded> ReadTail(Session session, string blobId, long maxLength, CancellationToken cancellationToken = default);
        [Get(nameof(Read))]
        Task<Base64Encoded> Read(Session session, string blobId, long start, long maxLength, CancellationToken cancellationToken = default);
    }
}
