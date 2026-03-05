using ActualChat.Media;

namespace ActualChat.Maui;

public static class NSItemProviderExt
{
    public static string ImplyMimeType(this NSItemProvider itemProvider)
    {
        foreach (var utType in itemProvider.RegisteredContentTypes) {
            var ext = utType.PreferredFilenameExtension;
            if (!ext.IsNullOrEmpty() && MediaMimeTypes.TryGetMimeType(ext, out var mimeType))
                return mimeType;

            var preferredMimeType = utType.PreferredMimeType;
            if (!preferredMimeType.IsNullOrEmpty() && MediaMimeTypes.TryGetExtension(preferredMimeType, out _))
                return preferredMimeType;
        }

        return "application/octet-stream";
    }
}
