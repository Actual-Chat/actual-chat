using System.IO;
using Stl.Text;
using Storage.Net;
using Storage.Net.Blobs;

namespace ActualChat.Blobs
{
    public class LocalBlobStorageProvider : IBlobStorageProvider
    {
        public IBlobStorage GetBlobStorage(Symbol blobScope) => StorageFactory.Blobs.DirectoryFiles(Path.Combine(Path.GetTempPath(), "blobs"));
    }
}