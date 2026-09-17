using ActualChat.Contacts;
using ActualChat.Maui.Services;
using Intents;

namespace ActualChat.Maui;

public static class IconUIExt
{
    private static ILogger Log => field ??= StaticLog.For(typeof(IconUIExt));

    extension(IconUI iconUI)
    {
        public async Task<INImage?> GetIntentImage(Contact contact, CancellationToken cancellationToken)
        {
            var iconQuery = contact.GetIconQuery(AvatarQuery.SupportedSizes[^1], renderAvatarTitle: true);
            var loadedImage = await iconUI.Get(iconQuery, cancellationToken).ConfigureAwait(false);
            if (loadedImage is null)
                return null;

            // Re-encoded through UIKit and embedded: the cache holds whatever the image proxy served
            // (WebP included), Intents renders a blank for data it can't decode, and iOS may purge the
            // cache directory a referenced file would live in.
            using var uiImage = UIImage.FromFile(loadedImage.FilePath);
            using var png = uiImage?.AsPNG();
            if (png is null) {
                Log.LogWarning("Avatar at '{FilePath}' isn't a decodable image", loadedImage.FilePath);
                return null;
            }

            return INImage.FromData(png);
        }
    }
}
