using ActualChat.Invite;

namespace ActualChat.UI.Blazor.App.Services;

public class LinkPreviewUI(AppUIHub hub) : UIServiceBase<AppUIHub>(hub), IComputeService
{
    private IChats Chats => Hub.Chats;
    private IAuthors Authors => Hub.Authors;
    private IPlaces Places => Hub.Places;
    private IAccounts Accounts => Hub.Accounts;
    private IInvites Invites => Hub.Invites;

    [ComputeMethod]
    public virtual async Task<bool> IsRenderedAsLocal(string url, CancellationToken cancellationToken)
    {
        var localLinkInfo = await TryGetLocal(url, cancellationToken).ConfigureAwait(false);
        return localLinkInfo?.CanRender == true;
    }

    [ComputeMethod]
    public virtual async Task<LocalLinkInfo?> TryGetLocal(string url, CancellationToken cancellationToken)
    {
        // The url comes from message markup, so it may carry anything
        if (LocalUrl.FromAbsolute(url, UrlMapper) is not { } localUrlOpt || !localUrlOpt.IsTrulyLocal())
            return null;

        return await GetLocal(localUrlOpt, cancellationToken).ConfigureAwait(false);
    }

    [ComputeMethod]
    public virtual async Task<LocalLinkInfo> GetLocal(LocalUrl localUrl, CancellationToken cancellationToken)
    {
        LocalLinkInfo? localLinkInfo = null;
        if (localUrl.IsChat())
            localLinkInfo = await GetLocalLinkForChat(localUrl, cancellationToken).ConfigureAwait(false);
        else if (localUrl.IsUser())
            localLinkInfo = await GetLocalLinkForUser(localUrl, cancellationToken).ConfigureAwait(false);
        else if (localUrl.IsPrivateChatInvite())
            localLinkInfo = await GetLocalLinkForPrivateChat(localUrl, cancellationToken).ConfigureAwait(false);
        return localLinkInfo ?? new LocalLinkInfo(localUrl);
    }

    private async Task<LocalLinkInfo?> GetLocalLinkForChat(LocalUrl localUrl, CancellationToken cancellationToken)
    {
        if (!localUrl.IsChatCompat(out var chatId, out var entryLid))
            return null;

        var chat = await Chats.Get(Session, chatId, cancellationToken).ConfigureAwait(false);
        if (chat is null)
            return null;

        var localLinkModel = new ChatLocalLinkInfo(localUrl, chat);
        if (entryLid > 0) {
            var chatEntryId = ChatEntryId.New(chatId, entryLid);
            var entry = await Chats.GetEntry(Session, chatEntryId, cancellationToken).ConfigureAwait(false);
            localLinkModel = localLinkModel with { Entry = entry };
            if (entry is not null) {
                var authorId = entry.AuthorId;
                var author = await Authors.Get(Session, entry.ChatId, authorId, cancellationToken)
                    .ConfigureAwait(false);
                localLinkModel = localLinkModel with { Author = author };
            }
        }
        if (chatId is PlaceChatId placeChatId) {
            var place = await Places.Get(Session, placeChatId.PlaceId, cancellationToken).ConfigureAwait(false);
            localLinkModel = localLinkModel with { Place = place };
        }
        return localLinkModel;
    }

    private async Task<LocalLinkInfo?> GetLocalLinkForUser(LocalUrl localUrl, CancellationToken cancellationToken)
    {
        if (!localUrl.IsUser(out var userIdSegment))
            return null;

        var userId = UserId.TryParse(userIdSegment);
        if (userId is null)
            return null;

        var account = await Accounts.Get(Session, userId, cancellationToken).ConfigureAwait(false);
        if (account is null)
            return null;

        var ownAccount = await AccountUI.OwnAccount.Use(cancellationToken).ConfigureAwait(false);
        Chat.Chat? chat = null;
        if (ownAccount.Id != userId) {
            var peerChatId = PeerChatId.New(ownAccount.Id, userId);
            chat = await Chats.Get(Session, peerChatId, cancellationToken).ConfigureAwait(false);
        }
        return new UserLocalLinkInfo(localUrl, account) {
            Chat = chat
        };
    }

    private async Task<LocalLinkInfo?> GetLocalLinkForPrivateChat(LocalUrl localUrl, CancellationToken cancellationToken)
    {
        if (!localUrl.IsPrivateChatInvite(out var inviteIdSegment))
            return null;

        var linkPreview = await Invites.GetInviteChatLinkPreview(Session, inviteIdSegment, cancellationToken)
            .ConfigureAwait(false);
        if (linkPreview is null)
            return null;

        if (linkPreview.Chat is not null)
            return new ChatLocalLinkInfo(localUrl, linkPreview.Chat) {
                Place = linkPreview.Place
            };

        if (linkPreview.Place is not null)
            return new PlaceLocalLinkInfo(localUrl, linkPreview.Place);

        return null;
    }
}
