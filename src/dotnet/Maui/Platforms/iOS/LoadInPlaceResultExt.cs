using ActualChat.Media;
using ActualLab.IO;
using UniformTypeIdentifiers;

namespace ActualChat.Maui;

public static class LoadInPlaceResultExt
{
    extension(LoadInPlaceResult representation)
    {
        public FilePath Path => representation.FileUrl.Path!;

        public string ImplyMimeType(NSItemProvider itemProvider)
            => representation.ImplyMimeType(itemProvider.RegisteredContentTypes);

        public string ImplyMimeType(IReadOnlyList<UTType> registeredContentTypes)
        {
            if (MediaMimeTypes.TryGetMimeType(representation.Path.FileName, out var mimeType))
                return mimeType;

            foreach (var utType in registeredContentTypes) {
                var ext = utType.PreferredFilenameExtension;
                if (!ext.IsNullOrEmpty() && MediaMimeTypes.TryGetMimeType(ext, out mimeType))
                    return mimeType;

                var preferredMimeType = utType.PreferredMimeType;
                if (!preferredMimeType.IsNullOrEmpty() && MediaMimeTypes.TryGetExtension(preferredMimeType, out _))
                    return preferredMimeType;
            }

            return "application/octet-stream";
        }

        public async Task Copy(FilePath targetPath)
        {
            Directory.CreateDirectory(targetPath.DirectoryPath);
            var source = File.OpenRead(representation.Path);
            await using var _1 = source.ConfigureAwait(false);
            var target = File.Open(targetPath, FileMode.Create, FileAccess.Write, FileShare.None);
            await using var _2 = target.ConfigureAwait(false);
            await source.CopyToAsync(target).ConfigureAwait(false);
        }
    }
}
