using ActualChat.Invite;
using ActualChat.UI.Blazor.Services;
using ActualChat.Users.UI.Blazor;

namespace ActualChat.Chat.UI.Blazor;

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

        var title = "Share chat";
        var text = $"\"{chat.Title}\" on Actual Chat";
        if (chat.IsPublic)
            return new ShareModalModel(
                ShareKind.Chat, title, chat.Title,
                new(text, Links.Chat(chat.Id)));

        var invites = hub.GetRequiredService<IInvites>();
        var invite = await invites.GetOrGenerateChatInvite(session, chat.Id, cancellationToken).ConfigureAwait(false);
        if (invite == null)
            return null;

        title = "Share private chat join link";
        return new ShareModalModel(
            ShareKind.ChatInvite, title, chat.Title,
            new(text, Links.Invite(InviteLinkFormat.PrivateChat, invite.Id)));
    }
}
