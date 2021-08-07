using Stl.Text;
using Storage.Net.Blobs;

namespace ActualChat.Blobs
{
    public interface IBlobStorageProvider
    {
        IBlobStorage GetBlobStorage(Symbol blobScope);
    }
}
