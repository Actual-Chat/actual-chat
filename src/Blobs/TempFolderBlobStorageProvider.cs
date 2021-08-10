using System.IO;
using Stl.IO;
using Stl.Text;
using Storage.Net;
using Storage.Net.Blobs;

namespace ActualChat.Blobs
{
    public class TempFolderBlobStorageProvider : IBlobStorageProvider
    {
        public IBlobStorage GetBlobStorage(Symbol blobScope)
        {
            var blobFolderPath = PathEx.GetApplicationTempDirectory() & "blobs";
            return StorageFactory.Blobs.DirectoryFiles(blobFolderPath);
        }
    }
}
