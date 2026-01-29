using ActualChat.Maui;
using UniformTypeIdentifiers;

namespace ActualChat.App.Maui.IosShareExt.Services;

public static class NSItemProviderExt
{
    private static ILogger Log => field ??= StaticLog.For(typeof(NSItemProviderExt));

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

        public async Task<T> Read<T>(UTType contentType)
            where T : NSObject
            => (T)await item.LoadItemAsync(contentType.Identifier, null).ConfigureAwait(false);

        public async Task<UploadInput> ToUploadInput()
        {
            var input = await GetInputFromInMemoryImage(item).ConfigureAwait(false);
            if (input is not null)
                return input;

            var inPlaceResult = await item.LoadInPlaceFileRepresentationAsync(UTTypes.Item.Identifier).ConfigureAwait(false);
            var fileName = inPlaceResult.GetSuggestedFileName(item);
            return new UploadInput(inPlaceResult.ImplyMimeType(item), fileName, File.OpenRead(inPlaceResult.Path));
        }

        private bool IsInMemoryImage()
            => item.HasItemConformingTo(UTTypes.Image.Identifier) && !item.HasItemConformingTo(UTTypes.FileUrl.Identifier);

        private async Task<UploadInput?> GetInputFromInMemoryImage()
        {
            if (!item.IsInMemoryImage())
                return null;

            var loadedItem = await item.LoadItemAsync(item.RegisteredContentTypes[0].Identifier, null).ConfigureAwait(false);
            // TODO: dispose image
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
}
