using ActualChat.Maui;
using ActualChat.UI.Services;
using ActualLab.IO;
using Microsoft.Maui.Storage;
using UniformTypeIdentifiers;

namespace ActualChat.App.Maui.IosShareExt.Services;

public static class NSItemProviderExt
{
    public static readonly IReadOnlyList<UTType> PlainTextTypes = [
        UTTypes.PlainText,
        UTTypes.Utf8PlainText,
        UTTypes.Utf16PlainText,
    ];
    public static readonly IReadOnlyList<UTType> TextTypes = PlainTextTypes.Concat([UTTypes.Url]).ToArray();

    extension(NSItemProvider item)
    {
        public bool HasText()
            // File URLs (like .txt files) should be treated as files, not as inline text
            => !item.HasItemConformingTo(UTTypes.FileUrl.Identifier) && item.RegisteredContentTypes.Intersect(TextTypes).Any();

        public async Task<string> GetText()
        {
            if (item.RegisteredContentTypes.Intersect(PlainTextTypes).Any())
                return await item.Read<NSString>(UTTypes.PlainText).ConfigureAwait(false);

            if (item.HasItemConformingTo(UTTypes.Url.Identifier)) {
                var url = await item.Read<NSUrl>(UTTypes.Url).ConfigureAwait(false);
                return url.ToString()!;
            }

            throw new InvalidOperationException("Unexpected content types: "
                + string.Join(", ", item.RegisteredContentTypes));
        }

        public async Task<T> Read<T>(UTType contentType)
            where T : NSObject
            => (T)await item.LoadItemAsync(contentType.Identifier, null).ConfigureAwait(false);

        public async Task<UploadSource> ToUploadSource()
        {
            var contentType = item.PickMainContentType();
            var source = await item.GetSourceFromInMemoryImage(contentType).ConfigureAwait(false);
            if (source is not null)
                return source;

            var inPlaceResult = await item.LoadInPlaceFileRepresentationAsync(contentType.Identifier).ConfigureAwait(false);
            var fileName = inPlaceResult.GetSuggestedFileName(item);
            var filePath = inPlaceResult.Path;
            var mimeType = contentType.PreferredMimeType.RequireNonEmpty();
            var metadata = new UploadSourceMetadata(mimeType, filePath.FileSize, fileName);
            return new UploadSource(metadata, new FileUploadSource(filePath));
        }

        private bool IsInMemoryImage()
            => item.HasItemConformingTo(UTTypes.Image.Identifier) && !item.HasItemConformingTo(UTTypes.FileUrl.Identifier);

        private async Task<UploadSource?> GetSourceFromInMemoryImage(UTType contentType)
        {
            if (!item.IsInMemoryImage())
                return null;

            using var loadedItem = await item.LoadItemAsync(contentType.Identifier, null).ConfigureAwait(false);
            switch (loadedItem) {
            case NSData data: {
                var ext = "." + (contentType.PreferredFilenameExtension.NullIfEmpty() ?? "png");
                return SaveToTempFile(item, () => data, ext);
            }
            case UIImage image:
                return SaveToTempFile(item, image.AsJPEG, ".jpg") ?? SaveToTempFile(item, image.AsPNG, ".png");
            default:
                return null;
            }
        }

        private static UploadSource? SaveToTempFile(NSItemProvider provider, Func<NSData?> getData, string ext)
        {
            using var data = getData();
            if (data is null)
                return null;

            FilePath fileName = provider.SuggestedName.NullIfEmpty() ?? "image";
            fileName = fileName.EnsureExt(ext);
            var outputDir = new FilePath(FileSystem.CacheDirectory) | "shared-images";
            Directory.CreateDirectory(outputDir);
            var filePath = (outputDir | fileName).ToUnique();
            data.Save(filePath, NSDataWritingOptions.Atomic, out var error);
            error.Assert($"Failed to save in-memory image to temp file '{filePath}':");
            var metadata = new UploadSourceMetadata(MediaMimeTypes.GetMimeType(fileName), filePath.FileSize, fileName);
            return new UploadSource(metadata, new FileUploadSource(filePath));
        }
    }
}
