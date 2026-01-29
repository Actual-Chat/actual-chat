using ActualChat.Media;
using ActualLab.IO;
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

    public static bool HasText(this NSItemProvider item)
        // File URLs (like .txt files) should be treated as files, not as inline text
        => !item.HasItemConformingTo(UTTypes.FileUrl.Identifier) && item.RegisteredContentTypes.Intersect(TextTypes).Any();

    public static async Task<string> GetText(this NSItemProvider item)
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

    public static async Task<T> Read<T>(this NSItemProvider item, UTType contentType)
        where T : NSObject
        => (T)await item.LoadItemAsync(contentType.Identifier, null).ConfigureAwait(false);

    public static async Task<UploadInput> ToUploadInput(this NSItemProvider item)
    {
        var input = await GetInputFromInMemoryImage(item).ConfigureAwait(false);
        if (input is not null)
            return input;

        var inPlaceResult = await item.LoadInPlaceFileRepresentationAsync(item.RegisteredContentTypes[0].Identifier).ConfigureAwait(false);
        FilePath path = inPlaceResult.FileUrl.Path!;
        FilePath fileName = item.SuggestedName.NullIfEmpty() ?? path.FileNameWithoutExtension;
        if (!fileName.HasExtension)
            fileName = fileName.ChangeExtension(path.Extension);
        return new UploadInput(ImplyContentType(), fileName, File.OpenRead(path));

        string ImplyContentType()
        {
            if (MediaMimeTypes.TryGetMimeType(path, out var mimeType))
                return mimeType;

            foreach (var utType in item.RegisteredContentTypes) {
                var ext = utType.PreferredFilenameExtension;
                if (!ext.IsNullOrEmpty() && MediaMimeTypes.TryGetMimeType(ext, out mimeType))
                    return mimeType;

                var preferredMimeType = utType.PreferredMimeType;
                if (!preferredMimeType.IsNullOrEmpty() && MediaMimeTypes.TryGetExtension(preferredMimeType, out _))
                    return preferredMimeType;
            }

            return "application/octet-stream";
        }
    }

    private static bool IsInMemoryImage(this NSItemProvider item)
        => item.RegisteredContentTypes[0].ConformsTo(UTTypes.Image) && !item.HasItemConformingTo(UTTypes.FileUrl.Identifier);

    private static async Task<UploadInput?> GetInputFromInMemoryImage(NSItemProvider item)
    {
        if (!item.IsInMemoryImage())
            return null;

        var loadedItem = await item.LoadItemAsync(item.RegisteredContentTypes[0].Identifier, null).ConfigureAwait(false);
        var (image, fileName) = loadedItem switch {
            UIImage uiImage => (uiImage, item.SuggestedName),
            NSData data => (UIImage.LoadFromData(data), item.SuggestedName),
            _ => (null, null),
        };

        if (image is null)
            return null;

        if (image.AsJPEG() is { } jpeg)
            return new UploadInput("image/jpeg", fileName.NullIfEmpty() ?? "image.jpg", jpeg.AsStream());

        if (image.AsPNG() is { } png)
            return new UploadInput("image/png", fileName.NullIfEmpty() ?? "image.png", png.AsStream());

        return null;
    }
}
