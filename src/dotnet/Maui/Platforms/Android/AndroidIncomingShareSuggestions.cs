using ActualChat.Maui.Services;
using ActualChat.UI.App.Services;
using ActualLab.Diagnostics;
using Android.Content;
using Android.Graphics;
using AndroidX.Core.Content.PM;
using AndroidX.Core.Graphics.Drawable;
using Microsoft.Maui.ApplicationModel;

namespace ActualChat.Maui;

public class AndroidIncomingShareSuggestions(IServiceProvider services) : IncomingShareSuggestions(services)
{
    private const int AdaptiveIconSafeZone = 108;
    private const int AdaptiveIconSize = AdaptiveIconSafeZone * 3 / 2;
    private const int AdaptiveIconInset = (AdaptiveIconSize - AdaptiveIconSafeZone) / 2;
    private const int MaxShortLabelLength = 25;

    private IconUI IconUI => field ??= Services.GetRequiredService<IconUI>();
    private UrlMapper UrlMapper => field ??= Services.UrlMapper();
    private ILogger? DebugLog => Log.IfEnabled(LogLevel.Information, Constants.DebugMode.ShareSuggestions);

    protected override async Task SuggestInternal(ContactId contactId, CancellationToken cancellationToken)
    {
        var contact = await Contacts.Get(Session, contactId, cancellationToken).Require().ConfigureAwait(false);
        var chat = contact.Chat;

        var iconCompat = await LoadIcon(contact, cancellationToken).ConfigureAwait(false);
        try {
            var person = new AndroidX.Core.App.Person.Builder()
                .SetName(chat.Title)!
                .SetKey(chat.Id.Value)!
                .SetIcon(iconCompat)!
                .Build();

            var context = Platform.AppContext;
            var localId = Links.Chat(chat.Id);
            var chatUrl = UrlMapper.ToAbsolute(localId);
            var intent = new Intent(Intent.ActionDefault);
            intent.SetData(Android.Net.Uri.Parse(chatUrl));

            var shortLabel = chat.Title.Length > MaxShortLabelLength
                ? chat.Title[..MaxShortLabelLength]
                : chat.Title;

            var shortcutInfo = new ShortcutInfoCompat.Builder(context, chat.Id.Value)
                .SetShortLabel(shortLabel)!
                .SetLongLabel(chat.Title)!
                .SetIcon(iconCompat)!
                .SetIntent(intent)!
                .SetLongLived(true)!
                .SetCategories(["chat.actual.app.category.SHARE_TARGET"])!
                .SetPerson(person)!
                .Build();

            ShortcutManagerCompat.PushDynamicShortcut(context, shortcutInfo);

            DebugLog?.LogInformation("Pushed dynamic shortcut for chat {ChatId}", chat.Id);
        }
        finally {
            iconCompat.Dispose();
        }
    }

    private async Task<IconCompat> LoadIcon(Contacts.Contact contact, CancellationToken cancellationToken)
    {
        try {
            var loadedImage = await IconUI.Get(
                contact.GetIconQuery(avatarSize: 160, renderAvatarTitle: true),
                cancellationToken
            ).ConfigureAwait(false);

            if (loadedImage is not null) {
                var bitmap = await BitmapFactory.DecodeFileAsync(loadedImage.FilePath).ConfigureAwait(false);
                if (bitmap is not null) {
                    try {
                        return IconCompat.CreateWithAdaptiveBitmap(ToAdaptiveIcon(bitmap))!;
                    }
                    finally {
                        bitmap.Recycle();
                    }
                }
            }
        }
        catch (Exception e) when (!e.IsCancellationOf(cancellationToken)) {
            Log.LogWarning(e, "Failed to load avatar for contact {ContactId}, using fallback icon", contact.Id);
        }

        var context = Platform.AppContext;
        return IconCompat.CreateWithResource(context, context.ApplicationInfo!.Icon)!;
    }

    private static Bitmap ToAdaptiveIcon(Bitmap source)
    {
        // CreateWithAdaptiveBitmap treats the whole bitmap as the icon canvas, but the
        // launcher mask reveals only the inner safe zone - the avatar is drawn into that
        // zone (center-cropped to a square) and the surrounding inset is left transparent.
        var result = Bitmap.CreateBitmap(AdaptiveIconSize, AdaptiveIconSize, Bitmap.Config.Argb8888!)!;
        using var canvas = new Canvas(result);
        using var paint = new Paint { AntiAlias = true, FilterBitmap = true };

        var side = Math.Min(source.Width, source.Height);
        var src = new Rect(
            (source.Width - side) / 2,
            (source.Height - side) / 2,
            (source.Width + side) / 2,
            (source.Height + side) / 2);
        var dest = new Rect(
            AdaptiveIconInset,
            AdaptiveIconInset,
            AdaptiveIconSize - AdaptiveIconInset,
            AdaptiveIconSize - AdaptiveIconInset);
        canvas.DrawBitmap(source, src, dest, paint);
        return result;
    }
}
