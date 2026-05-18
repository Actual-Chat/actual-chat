using System.ComponentModel;
using ActualChat.Chat;
using ActualChat.Contacts;
using ActualChat.Mcp.Auth;
using ActualChat.Mcp.Dtos;
using ActualChat.Users;
using ModelContextProtocol.Server;

namespace ActualChat.Mcp.Tools;

[McpServerToolType]
public sealed class DiscoveryTools(
    IContacts contacts,
    IChats chats,
    IPlaces places,
    IAccounts accounts,
    McpSessionAccessor sessions)
{
    private const int MaxLimit = 1024;

    [McpServerTool(Name = "list_group_chats", UseStructuredContent = true)]
    [Description("Lists group chats the caller has access to (excludes peer/place chats). " +
        "`afterId` is exclusive; pass null to start from the beginning. `limit` is capped at 1024.")]
    public async Task<ListChatsResult> ListGroupChats(
        [Description("Return chats with id > afterId. Use null to start from the beginning.")] string? afterId = null,
        [Description("Max chats to return; capped at 1024.")] int limit = 256,
        CancellationToken cancellationToken = default)
    {
        var session = sessions.Session;
        var contactIds = await contacts.ListIds(session, placeId: null, cancellationToken).ConfigureAwait(false);
        var chatIds = contactIds
            .Where(id => id.Kind == ContactKind.Chat && id.ChatId is GroupChatId)
            .Select(id => id.ChatId)
            .ToArray();
        var page = Page(chatIds, afterId, limit, chatId => chatId.Value);
        var infos = await Task.WhenAll(page.Select(id => ResolveChatInfo(session, id, cancellationToken)))
            .ConfigureAwait(false);
        return new ListChatsResult(infos.Where(i => i is not null).Cast<ChatInfo>().ToArray());
    }

    [McpServerTool(Name = "list_places", UseStructuredContent = true)]
    [Description("Lists places the caller has access to. " +
        "`afterId` is exclusive; pass null to start from the beginning. `limit` is capped at 1024.")]
    public async Task<ListPlacesResult> ListPlaces(
        [Description("Return places with id > afterId. Use null to start from the beginning.")] string? afterId = null,
        [Description("Max places to return; capped at 1024.")] int limit = 256,
        CancellationToken cancellationToken = default)
    {
        var session = sessions.Session;
        var placeIds = await contacts.ListPlaceIds(session, cancellationToken).ConfigureAwait(false);
        var page = Page(placeIds, afterId, limit, placeId => placeId.Value);
        var infos = await Task.WhenAll(page.Select(id => ResolvePlaceInfo(session, id, cancellationToken)))
            .ConfigureAwait(false);
        return new ListPlacesResult(infos.Where(i => i is not null).Cast<PlaceInfo>().ToArray());
    }

    [McpServerTool(Name = "list_place_chats", UseStructuredContent = true)]
    [Description("Lists chats inside a place the caller has access to. " +
        "`afterId` is exclusive; pass null to start from the beginning. `limit` is capped at 1024.")]
    public async Task<ListChatsResult> ListPlaceChats(
        [Description("The place id.")] string placeId,
        [Description("Return chats with id > afterId. Use null to start from the beginning.")] string? afterId = null,
        [Description("Max chats to return; capped at 1024.")] int limit = 256,
        CancellationToken cancellationToken = default)
    {
        var session = sessions.Session;
        var parsedPlaceId = PlaceId.Parse(placeId);
        var contactIds = await contacts.ListIds(session, parsedPlaceId, cancellationToken).ConfigureAwait(false);
        var chatIds = contactIds
            .Where(id => id.Kind == ContactKind.Chat)
            .Select(id => id.ChatId)
            .ToArray();
        var page = Page(chatIds, afterId, limit, chatId => chatId.Value);
        var infos = await Task.WhenAll(page.Select(id => ResolveChatInfo(session, id, cancellationToken)))
            .ConfigureAwait(false);
        return new ListChatsResult(infos.Where(i => i is not null).Cast<ChatInfo>().ToArray());
    }

    [McpServerTool(Name = "list_peer_chats", UseStructuredContent = true)]
    [Description("Lists peer (direct) chats the caller has with other users. " +
        "`afterId` is exclusive; pass null to start from the beginning. `limit` is capped at 1024.")]
    public async Task<ListChatsResult> ListPeerChats(
        [Description("Return chats with id > afterId. Use null to start from the beginning.")] string? afterId = null,
        [Description("Max chats to return; capped at 1024.")] int limit = 256,
        CancellationToken cancellationToken = default)
    {
        var session = sessions.Session;
        var ownAccount = await accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        var contactIds = await contacts.ListIds(session, placeId: null, cancellationToken).ConfigureAwait(false);
        var peerChatIds = contactIds
            .Where(id => id.Kind == ContactKind.User && id.ChatId is PeerChatId)
            .Select(id => (PeerChatId)id.ChatId)
            .ToArray();
        var page = Page(peerChatIds, afterId, limit, peerChatId => peerChatId.Value);
        var infos = await Task.WhenAll(page.Select(id =>
                ResolvePeerChatInfo(session, id, ownAccount.Id, cancellationToken)))
            .ConfigureAwait(false);
        return new ListChatsResult(infos.Where(i => i is not null).Cast<ChatInfo>().ToArray());
    }

    private async Task<ChatInfo?> ResolveChatInfo(Session session, ChatId chatId, CancellationToken cancellationToken)
    {
        var chat = await chats.Get(session, chatId, cancellationToken).ConfigureAwait(false);
        return chat is null ? null : new ChatInfo(chat.Id.Value, chat.IsPublic, chat.Title);
    }

    private async Task<ChatInfo?> ResolvePeerChatInfo(
        Session session, PeerChatId peerChatId, UserId ownUserId, CancellationToken cancellationToken)
    {
        var chat = await chats.Get(session, peerChatId, cancellationToken).ConfigureAwait(false);
        if (chat is null)
            return null;

        var title = chat.Title;
        if (title.IsNullOrEmpty()) {
            var otherUserId = peerChatId.AnotherUserId(ownUserId);
            var otherAccount = await accounts.Get(session, otherUserId, cancellationToken).ConfigureAwait(false);
            title = otherAccount?.Avatar?.Name ?? "";
        }
        return new ChatInfo(chat.Id.Value, chat.IsPublic, title);
    }

    private async Task<PlaceInfo?> ResolvePlaceInfo(Session session, PlaceId placeId, CancellationToken cancellationToken)
    {
        var place = await places.Get(session, placeId, cancellationToken).ConfigureAwait(false);
        return place is null ? null : new PlaceInfo(place.Id.Value, place.IsPublic, place.Title);
    }

    private static T[] Page<T>(IReadOnlyList<T> items, string? afterId, int limit, Func<T, string> idSelector)
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
