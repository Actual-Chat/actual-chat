using System.ComponentModel;
using ActualChat.Contacts;
using ActualChat.Invite;
using ActualChat.Mcp.Auth;
using ModelContextProtocol.Server;

namespace ActualChat.Mcp.Tools;

[McpServerToolType]
public sealed class McpChatTools(IServiceProvider services)
{
    private const int DefaultLimit = 256;
    private const int MaxLimit = 1024;

    private IContacts Contacts { get; } = services.GetRequiredService<IContacts>();
    private IChats Chats { get; } = services.GetRequiredService<IChats>();
    private IPlaces Places { get; } = services.GetRequiredService<IPlaces>();
    private IAccounts Accounts { get; } = services.GetRequiredService<IAccounts>();
    private IAuthors Authors { get; } = services.GetRequiredService<IAuthors>();
    private IRoles Roles { get; } = services.GetRequiredService<IRoles>();
    private IInvites Invites { get; } = services.GetRequiredService<IInvites>();
    private UrlMapper UrlMapper { get; } = services.GetRequiredService<UrlMapper>();
    private ICommander Commander { get; } = services.Commander();
    private McpSessionAccessor SessionAccessor { get; } = services.GetRequiredService<McpSessionAccessor>();

    private Session Session => SessionAccessor.Session;

    [McpServerTool(Name = "get_chat", UseStructuredContent = true)]
    [Description("Returns full info about a chat: kind, title, description, picture, place, " +
        "member count and the caller's permissions.")]
    public async Task<McpChatDetails> GetChat(
        [Description("The chat id.")] string chatId,
        CancellationToken cancellationToken = default)
    {
        var parsedChatId = ChatId.Parse(chatId);
        var chat = await Chats.Get(Session, parsedChatId, cancellationToken).Require().ConfigureAwait(false);
        return await ToDetails(chat, cancellationToken).ConfigureAwait(false);
    }

    [McpServerTool(Name = "create_chat", UseStructuredContent = true)]
    [Description("Creates a group chat (or a place chat when `placeId` is given). The caller becomes its owner.")]
    public async Task<McpChatDetails> CreateChat(
        [Description("Chat title.")] string title,
        [Description("True for a public chat anyone can join.")] bool isPublic,
        [Description("Chat description.")] string? description = null,
        [Description("Place id to create the chat in.")] string? placeId = null,
        [Description("Media id of an uploaded picture (finish_upload with purpose 'chat_picture').")]
        string? pictureMediaId = null,
        CancellationToken cancellationToken = default)
    {
        var diff = new ChatDiff {
            Title = title,
            IsPublic = isPublic,
            Description = description,
            PlaceId = PlaceId.ParseNullable(placeId),
            MediaId = MediaId.ParseNullable(pictureMediaId),
            Kind = placeId is null ? ChatKind.Group : null,
        };
        var command = new Chats_Change {
            Session = Session,
            ChatId = default,
            ExpectedVersion = null,
            Change = Change.Create(diff),
        };
        var chat = await Commander.Call(command, cancellationToken).ConfigureAwait(false);
        return await ToDetails(chat, cancellationToken).ConfigureAwait(false);
    }

    [McpServerTool(Name = "update_chat", UseStructuredContent = true)]
    [Description("Updates a chat's title, description, visibility and/or picture. Omitted fields keep their value.")]
    public async Task<McpChatDetails> UpdateChat(
        [Description("The chat id.")] string chatId,
        [Description("New title.")] string? title = null,
        [Description("New description.")] string? description = null,
        [Description("New visibility.")] bool? isPublic = null,
        [Description("Media id of an uploaded picture.")] string? pictureMediaId = null,
        CancellationToken cancellationToken = default)
    {
        var parsedChatId = ChatId.Parse(chatId);
        var diff = new ChatDiff {
            Title = title,
            Description = description,
            IsPublic = isPublic,
            MediaId = MediaId.ParseNullable(pictureMediaId),
        };
        var command = new Chats_Change {
            Session = Session,
            ChatId = parsedChatId,
            ExpectedVersion = null,
            Change = Change.Update(diff),
        };
        var chat = await Commander.Call(command, cancellationToken).ConfigureAwait(false);
        return await ToDetails(chat, cancellationToken).ConfigureAwait(false);
    }

    [McpServerTool(Name = "list_group_chats", UseStructuredContent = true)]
    [Description("Lists group chats the caller has access to (excludes peer/place chats). " +
        "`afterId` is exclusive; pass null to start from the beginning. `limit` is capped at 1024.")]
    public async Task<McpListChatsResult> ListGroupChats(
        [Description("Return chats with id > afterId. Use null to start from the beginning.")] string? afterId = null,
        [Description("Max chats to return; capped at 1024.")] int limit = DefaultLimit,
        CancellationToken cancellationToken = default)
    {
        var contactIds = await Contacts.ListIds(Session, placeId: null, cancellationToken).ConfigureAwait(false);
        var chatIds = contactIds
            .Where(id => id is { Kind: ContactKind.Chat, ChatId: GroupChatId })
            .Select(id => id.ChatId)
            .ToArray();
        var page = Page(chatIds, afterId, limit, chatId => chatId.Value);
        var infos = await Task.WhenAll(page.Select(id => ResolveChatInfo(id, cancellationToken)))
            .ConfigureAwait(false);
        return new McpListChatsResult(infos.Where(i => i is not null).Cast<McpChatInfo>().ToArray());
    }

    [McpServerTool(Name = "list_places", UseStructuredContent = true)]
    [Description("Lists places the caller has access to. " +
        "`afterId` is exclusive; pass null to start from the beginning. `limit` is capped at 1024.")]
    public async Task<McpListPlacesResult> ListPlaces(
        [Description("Return places with id > afterId. Use null to start from the beginning.")] string? afterId = null,
        [Description("Max places to return; capped at 1024.")] int limit = DefaultLimit,
        CancellationToken cancellationToken = default)
    {
        var placeIds = await Contacts.ListPlaceIds(Session, cancellationToken).ConfigureAwait(false);
        var page = Page(placeIds, afterId, limit, placeId => placeId.Value);
        var infos = await Task.WhenAll(page.Select(id => ResolvePlaceInfo(id, cancellationToken)))
            .ConfigureAwait(false);
        return new McpListPlacesResult(infos.Where(i => i is not null).Cast<McpPlaceInfo>().ToArray());
    }

    [McpServerTool(Name = "list_place_chats", UseStructuredContent = true)]
    [Description("Lists chats inside a place the caller has access to. " +
        "`afterId` is exclusive; pass null to start from the beginning. `limit` is capped at 1024.")]
    public async Task<McpListChatsResult> ListPlaceChats(
        [Description("The place id.")] string placeId,
        [Description("Return chats with id > afterId. Use null to start from the beginning.")] string? afterId = null,
        [Description("Max chats to return; capped at 1024.")] int limit = DefaultLimit,
        CancellationToken cancellationToken = default)
    {
        var parsedPlaceId = PlaceId.Parse(placeId);
        var contactIds = await Contacts.ListIds(Session, parsedPlaceId, cancellationToken).ConfigureAwait(false);
        var chatIds = contactIds
            .Where(id => id.Kind == ContactKind.Chat)
            .Select(id => id.ChatId)
            .ToArray();
        var page = Page(chatIds, afterId, limit, chatId => chatId.Value);
        var infos = await Task.WhenAll(page.Select(id => ResolveChatInfo(id, cancellationToken)))
            .ConfigureAwait(false);
        return new McpListChatsResult(infos.Where(i => i is not null).Cast<McpChatInfo>().ToArray());
    }

    [McpServerTool(Name = "list_peer_chats", UseStructuredContent = true)]
    [Description("Lists peer (direct) chats the caller has with other users. " +
        "`afterId` is exclusive; pass null to start from the beginning. `limit` is capped at 1024.")]
    public async Task<McpListChatsResult> ListPeerChats(
        [Description("Return chats with id > afterId. Use null to start from the beginning.")] string? afterId = null,
        [Description("Max chats to return; capped at 1024.")] int limit = DefaultLimit,
        CancellationToken cancellationToken = default)
    {
        var ownAccount = await Accounts.GetOwn(Session, cancellationToken).ConfigureAwait(false);
        var contactIds = await Contacts.ListIds(Session, placeId: null, cancellationToken).ConfigureAwait(false);
        var peerChatIds = contactIds
            .Where(id => id is { Kind: ContactKind.User, ChatId: PeerChatId })
            .Select(id => (PeerChatId)id.ChatId)
            .ToArray();
        var page = Page(peerChatIds, afterId, limit, peerChatId => peerChatId.Value);
        var infos = await Task.WhenAll(page.Select(id =>
                ResolvePeerChatInfo(id, ownAccount.Id, cancellationToken)))
            .ConfigureAwait(false);
        return new McpListChatsResult(infos.Where(i => i is not null).Cast<McpChatInfo>().ToArray());
    }

    [McpServerTool(Name = "list_members", UseStructuredContent = true)]
    [Description("Lists chat members. `userId` is null for anonymous members. " +
        "`afterId` is an exclusive author id; `limit` is capped at 1024.")]
    public async Task<McpListMembersResult> ListMembers(
        [Description("The chat id.")] string chatId,
        [Description("Return members with author id > afterId.")] string? afterId = null,
        [Description("Max members to return; capped at 1024.")] int limit = DefaultLimit,
        CancellationToken cancellationToken = default)
    {
        var parsedChatId = ChatId.Parse(chatId);
        var authorIds = await Authors.ListAuthorIds(Session, parsedChatId, cancellationToken).ConfigureAwait(false);
        var ownerIds = await Roles.ListOwnerIds(Session, parsedChatId, cancellationToken).ConfigureAwait(false);
        var page = Page(authorIds.OrderBy(id => id.Value).ToArray(), afterId, limit, id => id.Value);
        var members = await page
            .ToMcpMembers(
                id => Authors.Get(Session, parsedChatId, id, cancellationToken),
                id => Authors.GetAccount(Session, parsedChatId, id, cancellationToken),
                ownerIds.ToHashSet())
            .ConfigureAwait(false);
        return new McpListMembersResult(members);
    }

    [McpServerTool(Name = "add_members", UseStructuredContent = true)]
    [Description("Adds users to a chat directly (requires the Invite permission). " +
        "User ids come from get_me, list_members or list_peer_chats.")]
    public async Task AddMembers(
        [Description("The chat id.")] string chatId,
        [Description("User ids to add.")] string[] userIds,
        CancellationToken cancellationToken = default)
    {
        var command = new Authors_Invite {
            Session = Session,
            ChatId = ChatId.Parse(chatId),
            UserIds = userIds.Select(UserId.Parse).ToArray(),
        };
        await Commander.Call(command, cancellationToken).ConfigureAwait(false);
    }

    [McpServerTool(Name = "remove_member", UseStructuredContent = true)]
    [Description("Removes a member from a chat by author id (requires the EditMembers permission).")]
    public async Task RemoveMember(
        [Description("The chat id.")] string chatId,
        [Description("The member's author id (from list_members).")] string authorId,
        CancellationToken cancellationToken = default)
    {
        var parsedAuthorId = AuthorId.Parse(authorId);
        if (parsedAuthorId.ChatId != ChatId.Parse(chatId))
            throw StandardError.Constraint("authorId does not belong to chatId.");

        var command = new Authors_Exclude { Session = Session, AuthorId = parsedAuthorId };
        await Commander.Call(command, cancellationToken).ConfigureAwait(false);
    }

    [McpServerTool(Name = "join_chat", UseStructuredContent = true)]
    [Description("Joins a public chat (or a chat the caller was invited to).")]
    public async Task JoinChat(
        [Description("The chat id.")] string chatId,
        CancellationToken cancellationToken = default)
    {
        var command = new Authors_Join { Session = Session, ChatId = ChatId.Parse(chatId) };
        await Commander.Call(command, cancellationToken).ConfigureAwait(false);
    }

    [McpServerTool(Name = "leave_chat", UseStructuredContent = true)]
    [Description("Leaves a chat.")]
    public async Task LeaveChat(
        [Description("The chat id.")] string chatId,
        CancellationToken cancellationToken = default)
    {
        var command = new Authors_Leave { Session = Session, ChatId = ChatId.Parse(chatId) };
        await Commander.Call(command, cancellationToken).ConfigureAwait(false);
    }

    [McpServerTool(Name = "create_invite_link", UseStructuredContent = true)]
    [Description("Returns the chat's current invite link, generating one if none is active " +
        "(requires the Invite permission).")]
    public async Task<McpInviteLink> CreateInviteLink(
        [Description("The chat id.")] string chatId,
        CancellationToken cancellationToken = default)
    {
        var invite = await Invites.GetOrGenerateChatInvite(Session, ChatId.Parse(chatId), cancellationToken)
            .Require()
            .ConfigureAwait(false);
        return invite.ToMcpModel(UrlMapper);
    }

    [McpServerTool(Name = "list_invite_links", UseStructuredContent = true)]
    [Description("Lists the chat's active invite links (revoked and exhausted ones are not returned).")]
    public async Task<McpInviteLink[]> ListInviteLinks(
        [Description("The chat id.")] string chatId,
        CancellationToken cancellationToken = default)
    {
        var invites = await Invites.ListChatInvites(Session, ChatId.Parse(chatId), cancellationToken)
            .ConfigureAwait(false);
        return invites.Select(i => i.ToMcpModel(UrlMapper)).ToArray();
    }

    [McpServerTool(Name = "revoke_invite_link", UseStructuredContent = true)]
    [Description("Revokes a chat or place invite link.")]
    public async Task RevokeInviteLink(
        [Description("The invite id.")] string inviteId,
        CancellationToken cancellationToken = default)
    {
        var command = new Invites_Revoke { Session = Session, InviteId = inviteId };
        await Commander.Call(command, cancellationToken).ConfigureAwait(false);
    }

    // Private methods

    private async Task<McpChatDetails> ToDetails(Chat.Chat chat, CancellationToken cancellationToken)
    {
        // The command result may carry no Rules; re-read through the session to get the caller's permissions
        chat = await Chats.Get(Session, chat.Id, cancellationToken).Require().ConfigureAwait(false);
        var authorIds = await Authors.ListAuthorIds(Session, chat.Id, cancellationToken).ConfigureAwait(false);
        return chat.ToMcpDetails(authorIds.Length, UrlMapper);
    }

    private async Task<McpChatInfo?> ResolveChatInfo(ChatId chatId, CancellationToken cancellationToken)
    {
        var chat = await Chats.Get(Session, chatId, cancellationToken).ConfigureAwait(false);
        return chat is null ? null : new McpChatInfo(chat.Id.Value, chat.IsPublic, chat.Title);
    }

    private async Task<McpChatInfo?> ResolvePeerChatInfo(
        PeerChatId peerChatId, UserId ownUserId, CancellationToken cancellationToken)
    {
        var chat = await Chats.Get(Session, peerChatId, cancellationToken).ConfigureAwait(false);
        if (chat is null)
            return null;

        var title = chat.Title;
        if (title.IsNullOrEmpty()) {
            var otherUserId = peerChatId.AnotherUserId(ownUserId);
            var otherAccount = await Accounts.Get(Session, otherUserId, cancellationToken).ConfigureAwait(false);
            title = otherAccount?.Avatar?.Name ?? "";
        }
        return new McpChatInfo(chat.Id.Value, chat.IsPublic, title);
    }

    private async Task<McpPlaceInfo?> ResolvePlaceInfo(PlaceId placeId, CancellationToken cancellationToken)
    {
        var place = await Places.Get(Session, placeId, cancellationToken).ConfigureAwait(false);
        return place is null ? null : new McpPlaceInfo(place.Id.Value, place.IsPublic, place.Title);
    }

    internal static T[] Page<T>(IReadOnlyList<T> items, string? afterId, int limit, Func<T, string> idSelector)
    {
        limit = Math.Clamp(limit, 1, MaxLimit);
        var startIndex = 0;
        if (!afterId.IsNullOrEmpty()) {
            for (var i = 0; i < items.Count; i++) {
                if (string.Equals(idSelector(items[i]), afterId, StringComparison.Ordinal)) {
                    startIndex = i + 1;
                    break;
                }
            }
        }
        if (startIndex >= items.Count)
            return [];

        var count = Math.Min(limit, items.Count - startIndex);
        var result = new T[count];
        for (var i = 0; i < count; i++)
            result[i] = items[startIndex + i];
        return result;
    }
}
