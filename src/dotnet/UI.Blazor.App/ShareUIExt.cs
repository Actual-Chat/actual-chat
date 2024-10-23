using ActualChat.Invite;
using ActualChat.UI.Blazor.Services;
using Cysharp.Text;

namespace ActualChat.UI.Blazor.App;

public static class ShareUIExt
{
    public static async Task<ModalRef?> Share(
        this ShareUI shareUI, ChatId chatId, CancellationToken cancellationToken = default)
    {
        var shareModel = await shareUI.GetModel(chatId, cancellationToken).ConfigureAwait(true);
        return shareModel == null ? null
            : await shareUI.Share(shareModel).ConfigureAwait(false);
    }

    public static async ValueTask<ShareModalModel?> GetModel(
        this ShareUI shareUI, ChatId chatId, CancellationToken cancellationToken = default)
    {
        var hub = shareUI.Hub;
        var session = hub.Session();
        var chats = hub.GetRequiredService<IChats>();
        var chat = await chats.Get(session, chatId, cancellationToken).ConfigureAwait(false);
        if (chat?.HasSingleAuthor != false)
            return null;

        if (chat.Id.IsPeerChat(out var peerChatId)) {
            var accountUI = hub.GetRequiredService<AccountUI>();
            await accountUI.WhenLoaded.WaitAsync(cancellationToken).ConfigureAwait(false);
            var ownAccount = accountUI.OwnAccount.Value;
            if (ownAccount.IsGuestOrNone)
                return null;

            var otherUserId = peerChatId.UserIds.OtherThanOrDefault(ownAccount.Id);
            return otherUserId.IsNone ? null
                : await shareUI.GetModel(otherUserId, cancellationToken).ConfigureAwait(false);
        }

        Place? place = null;
        if (!chatId.PlaceChatId.IsNone) {
            var places = hub.GetRequiredService<IPlaces>();
            place = await places.Get(session, chatId.PlaceChatId.PlaceId, cancellationToken).ConfigureAwait(false);
            if (place is null)
                return null; // We should be able to get chat's place. Return null if it's not like that.
        }

        var targetTitle = place is null ? chat.Title : ZString.Concat(place.Title, "/", chat.Title);
        var text = $"\"{targetTitle}\" on Actual Chat";
        if ((place is null || place.IsPublic) && chat.IsPublic)
            return new ShareModalModel(
                ShareKind.Chat,
                "Share chat",
                targetTitle,
                new (text, Links.Chat(chat.Id)),
                null);

        var invites = hub.GetRequiredService<IInvites>();
        var invite = await invites.GetOrGenerateChatInvite(session, chat.Id, cancellationToken)
            .ConfigureAwait(false);
        if (invite == null)
            return null;

        var shareModalSelectorPrefs = ShareWithPlaceMembersOnly.GetFor(chat, place);
        return new ShareModalModel(
            ShareKind.ChatInvite,
            "Share private chat join link",
            targetTitle,
            new (text, Links.Invite(InviteLinkFormat.PrivateChat, invite.Id)),
            shareModalSelectorPrefs);
    }

    public static async ValueTask<ShareModalModel?> GetModel(
        this ShareUI shareUI, PlaceId placeId, CancellationToken cancellationToken = default)
    {
        var hub = shareUI.Hub;
        var session = hub.Session();
        var places = hub.GetRequiredService<IPlaces>();
        var place = await places.Get(session, placeId, cancellationToken).ConfigureAwait(false);
        if (place == null)
            return null;

        var text = $"\"{place.Title}\" on Actual Chat";
        if (place.IsPublic) {
            var welcomeChatId = await places.GetWelcomeChatId(session, placeId, cancellationToken).ConfigureAwait(false);
            // NOTE(DF): Direct navigation to place does not work well so far. Let's share place via welcome chat link.
            if (welcomeChatId.IsNone)
                return null;

            return new ShareModalModel(
                ShareKind.Place,
                "Share place",
                place.Title,
                new (text, Links.Chat(welcomeChatId)),
                null);
        }

        var invites = hub.GetRequiredService<IInvites>();
        var invite = await invites.GetOrGeneratePlaceInvite(session, place.Id, cancellationToken).ConfigureAwait(false);
        if (invite == null)
            return null;

        return new ShareModalModel(
            ShareKind.PlaceInvite,
            "Share private place join link",
            place.Title,
            new(text, Links.Invite(InviteLinkFormat.PrivatePlace, invite.Id)),
            null);
    }
}
