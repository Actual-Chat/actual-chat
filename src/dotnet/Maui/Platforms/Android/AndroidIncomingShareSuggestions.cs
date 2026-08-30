using ActualChat.Maui.Services;
using ActualChat.UI.App.Services;
using ActualLab.Diagnostics;
using Android.Graphics;
using AndroidX.Core.Graphics.Drawable;
using Microsoft.Maui.ApplicationModel;

namespace ActualChat.Maui;

public class AndroidIncomingShareSuggestions(IServiceProvider services) : IncomingShareSuggestions(services)
{
    private IconUI IconUI => field ??= Services.GetRequiredService<IconUI>();
    private UrlMapper UrlMapper => field ??= Services.UrlMapper();
    private ILogger? DebugLog => Log.IfEnabled(LogLevel.Information, Constants.DebugMode.ShareSuggestions);

    protected override async Task SuggestInternal(ContactId contactId, CancellationToken cancellationToken)
    {
        var contact = await Contacts.Get(Session, contactId, cancellationToken).Require().ConfigureAwait(false);
        var chat = contact.Chat;

        var iconCompat = await LoadIcon(contact, cancellationToken).ConfigureAwait(false);
        try {
            var chatUrl = UrlMapper.ToAbsolute(Links.Chat(chat.Id));
            AndroidChatShortcuts.Push(Platform.AppContext, chat.Id, chat.Title, chatUrl, iconCompat);
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
                        return AndroidChatShortcuts.CreateIcon(bitmap);
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
}
