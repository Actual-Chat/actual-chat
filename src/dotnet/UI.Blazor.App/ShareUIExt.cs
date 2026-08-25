using ActualChat.Invite;
using ActualChat.UI.Blazor.Resources;
using ActualChat.UI.Blazor.Services;

namespace ActualChat.UI.Blazor.App;

public static class ShareUIExt
{
    // ShareXxx

    public static async Task<ModalRef?> Share(
        this ShareUI shareUI, ChatId chatId, CancellationToken cancellationToken = default)
    {
        var shareModel = await shareUI.GetModel(chatId, cancellationToken).ConfigureAwait(true);
        return shareModel == null ? null
            : await shareUI.Share(shareModel).ConfigureAwait(false);
    }

    public static async Task<ModalRef?> Share(
        this ShareUI shareUI, UserId userId, CancellationToken cancellationToken = default)
    {
        var shareModel = await shareUI.GetModel(userId, cancellationToken).ConfigureAwait(true);
        return shareModel == null ? null
            : await shareUI.Share(shareModel).ConfigureAwait(false);
    }

    public static async Task<ModalRef?> Share(
        this ShareUI shareUI, PlaceId placeId, CancellationToken cancellationToken = default)
    {
        var shareModel = await shareUI.GetModel(placeId, cancellationToken).ConfigureAwait(true);
        return shareModel == null ? null
            : await shareUI.Share(shareModel).ConfigureAwait(false);
    }

    public static async Task<ModalRef?> ShareOwnAccount(
        this ShareUI shareUI, CancellationToken cancellationToken = default)
    {
        var shareModel = await shareUI.GetOwnAccountModel(cancellationToken).ConfigureAwait(true);
        return shareModel == null ? null
            : await shareUI.Share(shareModel).ConfigureAwait(false);
    }

    // GetXxxModel

    public static async ValueTask<ShareModalModel?> GetModel(
        this ShareUI shareUI, ChatId chatId, CancellationToken cancellationToken = default)
    {
        var hub = shareUI.Hub;
        var services = hub.Services;
        var session = hub.Session;
        var l = hub.StringLocalizer;
        var chats = services.GetRequiredService<IChats>();
        var chat = await chats.Get(session, chatId, cancellationToken).ConfigureAwait(false);
        if (chat is null || chat.HasSingleAuthor)
            return null;

        if (chatId is PeerChatId peerChatId) {
            var accountUI = services.GetRequiredService<AccountUI>();
            await accountUI.WhenReady.WaitAsync(cancellationToken).ConfigureAwait(false);
            var ownAccount = accountUI.OwnAccount.Value;
            if (ownAccount.IsGuestOrNull())
                return null;

            var otherUserId = peerChatId.AnotherUserIdOrNull(ownAccount.Id);
            return otherUserId is null ? null
                : await shareUI.GetModel(otherUserId, cancellationToken).ConfigureAwait(false);
        }

        Place? place = null;
        if (chatId is PlaceChatId placeChatId) {
            var places = services.GetRequiredService<IPlaces>();
            place = await places.Get(session, placeChatId.PlaceId, cancellationToken).ConfigureAwait(false);
            if (place is null)
                return null; // We should be able to get chat's place. Return null if it's not like that.
        }

        var targetTitle = place is null
            ? chat.Title
            : string.Concat(place.Title, "/", chat.Title);
        var text = l.Share_TextTitled_Format(targetTitle, CoreConstants.AppName);
        if ((place is null || place.IsPublic) && chat.IsPublic) {
            var localUrl = Links.Chat(chat.AliasInfo, place?.AliasInfo);
            return new ShareModalModel(
                ShareKind.Chat,
                l.Share_Chat,
                targetTitle,
                new (text, localUrl),
                null);
        }

        var invites = services.GetRequiredService<IInvites>();
        var invite = await invites.GetOrGenerateChatInvite(session, chat.Id, cancellationToken)
            .ConfigureAwait(false);
        if (invite == null)
            return null;

        var shareModalSelectorPrefs = ShareWithPlaceMembersOnly.GetFor(chat, place);
        return new ShareModalModel(
            ShareKind.ChatInvite,
            l.Share_PrivateChatJoinLink,
            targetTitle,
            new (text, Links.Invite(InviteLinkFormat.PrivateChat, invite.Id)),
            shareModalSelectorPrefs);
    }

    public static async ValueTask<ShareModalModel?> GetModel(
        this ShareUI shareUI, PlaceId placeId, CancellationToken cancellationToken = default)
    {
        var hub = shareUI.Hub;
        var services = hub.Services;
        var session = hub.Session;
        var l = hub.StringLocalizer;
        var places = services.GetRequiredService<IPlaces>();
        var place = await places.Get(session, placeId, cancellationToken).ConfigureAwait(false);
        if (place == null)
            return null;

        var text = l.Share_TextTitled_Format(place.Title, CoreConstants.AppName);
        if (place.IsPublic) {
            var welcomeChatId = await places.GetWelcomeChatId(session, placeId, cancellationToken).ConfigureAwait(false);
            // NOTE(DF): Direct navigation to place does not work well so far. Let's share place via welcome chat link.
            if (welcomeChatId is null)
                return null;

            return new ShareModalModel(
                ShareKind.Place,
                l.Share_Place,
                place.Title,
                new (text, Links.Chat(welcomeChatId)),
                null);
        }

        var invites = services.GetRequiredService<IInvites>();
        var invite = await invites.GetOrGeneratePlaceInvite(session, place.Id, cancellationToken).ConfigureAwait(false);
        if (invite == null)
            return null;

        return new ShareModalModel(
            ShareKind.PlaceInvite,
            l.Share_PrivatePlaceJoinLink,
            place.Title,
            new(text, Links.Invite(InviteLinkFormat.PrivatePlace, invite.Id)),
            null);
    }

    public static async ValueTask<ShareModalModel?> GetModel(
        this ShareUI shareUI, UserId userId, CancellationToken cancellationToken = default)
    {
        if (userId.IsGuestOrNull())
            return null;

        var hub = shareUI.Hub;
        var services = hub.Services;
        var session = hub.Session;

        var accountUI = hub.AccountUI;
        await accountUI.WhenReady.WaitAsync(cancellationToken).ConfigureAwait(false);
        var ownAccount = accountUI.OwnAccount.Value;
        if (userId == ownAccount.Id)
            return await shareUI.GetOwnAccountModel(cancellationToken).ConfigureAwait(false);

        var accounts = services.GetRequiredService<IAccounts>();
        var account = await accounts.Get(session, userId, cancellationToken).ConfigureAwait(false);
        if (account == null)
            return null;

        var l = hub.StringLocalizer;
        var name = account.Avatar.Name;
        var title = l.Share_ContactOf_Format(name);
        var text = l.Share_TextNamed_Format(name, CoreConstants.AppName);
        return new ShareModalModel(
            ShareKind.Contact, title, name,
            new(text, Links.User(account.Id)),
            null);
    }

    public static async ValueTask<ShareModalModel?> GetOwnAccountModel(
        this ShareUI shareUI, CancellationToken cancellationToken = default)
    {
        var accountUI = shareUI.Hub.AccountUI;
        await accountUI.WhenReady.WaitAsync(cancellationToken).ConfigureAwait(false);
        var ownAccount = accountUI.OwnAccount.Value;
        if (ownAccount.IsGuestOrNull())
            return null;

        var l = shareUI.Hub.StringLocalizer;
        var name = ownAccount.Avatar.Name;
        var title = l.YourAccount_ShareYourContact;
        var text = l.Share_TextNamed_Format(name, CoreConstants.AppName);
        return new ShareModalModel(
            ShareKind.Contact, title, name,
            new(text, Links.User(ownAccount.AliasInfo)),
            null);
    }
}
