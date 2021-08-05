using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Stl.DependencyInjection;
using Stl.Text;
using Storage.Net;

namespace ActualChat.Storage
{
    [RegisterService(typeof(IBlobStorage))]
    public class LocalAudioBlobStorage : IBlobStorage
    {
        private readonly global::Storage.Net.Blobs.IBlobStorage _blobStorage;

        public LocalAudioBlobStorage(StorageSettings settings)
        {
            var pathPrefix = string.IsNullOrEmpty(settings.LocalStoragePath) ? Path.GetTempPath() : settings.LocalStoragePath;
            var path = Path.GetFullPath($"{pathPrefix}/audio/");
            _blobStorage = StorageFactory.Blobs.DirectoryFiles(path);
        }

        public async Task Write(Symbol blobId, Stream stream, CancellationToken cancellationToken = default)
        {
            if (blobId.IsEmpty) return;

            await _blobStorage.WriteAsync(GetBlobPath(blobId), stream, false, cancellationToken);
        }

        public async Task<Stream> Read(Symbol blobId, CancellationToken cancellationToken = default)
        {
            if (blobId.IsEmpty) return Stream.Null;

            return await _blobStorage.OpenReadAsync(GetBlobPath(blobId), cancellationToken);
        }

        public async Task Delete(Symbol blobId, CancellationToken cancellationToken = default)
        {
            if (blobId.IsEmpty) return;

            await _blobStorage.DeleteAsync(new []{ GetBlobPath(blobId) }, cancellationToken);
        }

        private static string GetBlobPath(Symbol blobId)
        {
            var hi = "_";
            var lo = "_";
            if (blobId.Value.Length == 26)
            {
                var offset = Ulid.Parse(blobId).Time;
                hi = offset.Date.ToString("yyyy-MM-dd");
                lo = $"{offset.Hour}-{offset.Minute}";
            }

            var blobPath = $"{hi}/{lo}";
            return blobPath;
        }
    }
}
