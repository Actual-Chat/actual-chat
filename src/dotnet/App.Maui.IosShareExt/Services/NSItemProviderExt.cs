using ActualChat.Maui;
using ActualChat.UI.Services;
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
                return url.ToString();
            }

            throw new InvalidOperationException("Unexpected content types: "
                + string.Join(", ", item.RegisteredContentTypes));
        }

        public string ImplyMimeType()
        {
            var registeredContentTypes = item.RegisteredContentTypes;
            foreach (var utType in registeredContentTypes) {
                var ext = utType.PreferredFilenameExtension;
                if (!ext.IsNullOrEmpty() && MediaMimeTypes.TryGetMimeType(ext, out var mimeType))
                    return mimeType;

                var preferredMimeType = utType.PreferredMimeType;
                if (!preferredMimeType.IsNullOrEmpty() && MediaMimeTypes.TryGetExtension(preferredMimeType, out _))
                    return preferredMimeType;
            }

            return "application/octet-stream";
        }

        public async Task<T> Read<T>(UTType contentType)
            where T : NSObject
            => (T)await item.LoadItemAsync(contentType.Identifier, null).ConfigureAwait(false);

        public async Task<UploadSource> ToUploadSource()
        {
            var source = await item.GetSourceFromInMemoryImage().ConfigureAwait(false);
            if (source is not null)
                return source;

            var inPlaceResult = await item.LoadInPlaceFileRepresentationAsync(UTTypes.Item.Identifier).ConfigureAwait(false);
            var fileName = inPlaceResult.GetSuggestedFileName(item);
            var filePath = inPlaceResult.Path;
            var fileInfo = new FileInfo(filePath);
            var metadata = new UploadSourceMetadata(
                inPlaceResult.ImplyMimeType(item),
                fileInfo.Length,
                fileName);
            return new UploadSource(metadata, new FileUploadSource(filePath));
        }

        private bool IsInMemoryImage()
            => item.HasItemConformingTo(UTTypes.Image.Identifier) && !item.HasItemConformingTo(UTTypes.FileUrl.Identifier);

        private async Task<UploadSource?> GetSourceFromInMemoryImage()
        {
            if (!item.IsInMemoryImage())
                return null;

            var loadedItem = await item.LoadItemAsync(item.RegisteredContentTypes[0].Identifier, null).ConfigureAwait(false);
            var (image, fileName) = loadedItem switch {
                UIImage uiImage => (uiImage, item.SuggestedName),
                NSData data => (UIImage.LoadFromData(data), item.SuggestedName),
                _ => (null, null),
            };

            if (image is null) {
                loadedItem.DisposeSilently();
                return null;
            }

            try {
                if (image.AsJPEG() is { } jpeg) {
                    var bytes = jpeg.ToArray();
                    jpeg.DisposeSilently();
                    var metadata = new UploadSourceMetadata("image/jpeg", bytes.Length, fileName.NullIfEmpty() ?? "image.jpg");
                    return new UploadSource(metadata, new StreamUploadSource(() => Task.FromResult<Stream>(new MemoryStream(bytes))));
                }

                if (image.AsPNG() is { } png) {
                    var bytes = png.ToArray();
                    png.DisposeSilently();
                    var metadata = new UploadSourceMetadata("image/png", bytes.Length, fileName.NullIfEmpty() ?? "image.png");
                    return new UploadSource(metadata, new StreamUploadSource(() => Task.FromResult<Stream>(new MemoryStream(bytes))));
                }

                return null;
            }
            finally {
                image.DisposeSilently();
            }
        }
    }
}
