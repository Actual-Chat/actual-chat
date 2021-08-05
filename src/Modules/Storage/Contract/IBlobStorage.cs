using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Stl.Text;

namespace ActualChat.Storage
{
    public interface IBlobStorage
    {
        Task Write(Symbol blobId, Stream stream, CancellationToken cancellationToken = default);
        
        Task<Stream> Read(Symbol blobId, CancellationToken cancellationToken = default);
        
        Task Delete(Symbol blobId, CancellationToken cancellationToken = default);
    }
}