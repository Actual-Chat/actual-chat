using System.ComponentModel;
using ActualChat.Contacts;
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
    private McpSessionAccessor SessionAccessor { get; } = services.GetRequiredService<McpSessionAccessor>();

    private Session Session => SessionAccessor.Session;

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

    // Private methods

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
