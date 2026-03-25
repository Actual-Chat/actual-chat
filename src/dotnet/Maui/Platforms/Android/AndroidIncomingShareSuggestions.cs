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
    private const int AdaptiveIconSize = 108;
    private const int MaxShortLabelLength = 25;
    private int _nextRank;

    private IconUI IconUI => field ??= Services.GetRequiredService<IconUI>();
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
            var chatUrl = $"https://{MauiSettings.Host}/chat/{chat.Id}";
            var intent = new Intent(Intent.ActionDefault);
            intent.SetData(Android.Net.Uri.Parse(chatUrl));

            var categories = new HashSet<string> {
                "com.android.sharing_shortcut.TEXT",
                "com.android.sharing_shortcut.IMAGE",
            };

            var shortLabel = chat.Title.Length > MaxShortLabelLength
                ? chat.Title[..MaxShortLabelLength]
                : chat.Title;

            var rank = Interlocked.Increment(ref _nextRank);
            var shortcutInfo = new ShortcutInfoCompat.Builder(context, chat.Id.Value)!
                .SetShortLabel(shortLabel)!
                .SetLongLabel(chat.Title)!
                .SetIcon(iconCompat)!
                .SetIntent(intent)!
                .SetLongLived(true)!
                .SetCategories(categories)!
                .SetPerson(person)!
                .SetRank(rank)!
                .Build();

            await MainThread.InvokeOnMainThreadAsync(
                () => ShortcutManagerCompat.PushDynamicShortcut(context, shortcutInfo)
            ).ConfigureAwait(false);

            DebugLog?.LogInformation("Pushed dynamic shortcut for chat {ChatId}", chat.Id);
        }
        finally {
            iconCompat?.Dispose();
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
                    var scaled = Bitmap.CreateScaledBitmap(bitmap, AdaptiveIconSize, AdaptiveIconSize, true);
                    if (!ReferenceEquals(scaled, bitmap))
                        bitmap.Recycle();
                    return IconCompat.CreateWithAdaptiveBitmap(scaled)!;
                }
            }
        }
        catch (Exception e) when (!e.IsCancellationOf(cancellationToken)) {
            Log.LogWarning(e, "Failed to load avatar for contact {ContactId}, using fallback icon", contact.Id);
        }

        var context = Platform.AppContext;
        return IconCompat.CreateWithResource(context, context.ApplicationInfo!.Icon)!;
    }
}
