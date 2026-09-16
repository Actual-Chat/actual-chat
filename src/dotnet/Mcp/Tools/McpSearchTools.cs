using System.ComponentModel;
using ActualChat.Contacts;
using ActualChat.Mcp.Auth;
using ActualChat.Search;
using ModelContextProtocol.Server;

namespace ActualChat.Mcp.Tools;

[McpServerToolType]
public sealed class McpSearchTools(IServiceProvider services)
{
    private const int DefaultLimit = 20;
    private const int MaxLimit = 100;

    // ISearch is registered only when MLSearch is enabled; resolving it lazily lets the other tools work regardless
    private ISearch Search => field ??= services.GetRequiredService<ISearch>();
    private IChats Chats { get; } = services.GetRequiredService<IChats>();
    private IPlaces Places { get; } = services.GetRequiredService<IPlaces>();
    private IAccounts Accounts { get; } = services.GetRequiredService<IAccounts>();
    private McpSessionAccessor SessionAccessor { get; } = services.GetRequiredService<McpSessionAccessor>();

    private Session Session => SessionAccessor.Session;

    [McpServerTool(Name = "search_messages", UseStructuredContent = true)]
    [Description("Full-text search over messages the caller can read, optionally limited to a chat or a place.")]
    public async Task<McpFoundMessage[]> SearchMessages(
        [Description("Search text.")] string query,
        [Description("Limit to this chat.")] string? chatId = null,
        [Description("Limit to this place.")] string? placeId = null,
        [Description("Max results; capped at 100.")] int limit = DefaultLimit,
        CancellationToken cancellationToken = default)
    {
        var searchQuery = new EntrySearchQuery {
            Criteria = query,
            ChatId = ChatId.ParseNullable(chatId),
            PlaceId = PlaceId.ParseNullable(placeId),
            Limit = Math.Clamp(limit, 1, MaxLimit),
        };
        var result = await Search.FindEntries(Session, searchQuery, cancellationToken).ConfigureAwait(false);
        return result.Items
            .Select(i => new McpFoundMessage(i.EntryId.ChatId.Value, i.EntryId.LocalId, i.Text))
            .ToArray();
    }

    [McpServerTool(Name = "search_contacts", UseStructuredContent = true)]
    [Description("Searches people, group chats or places by name. `scope`: people | groups | places. " +
        "`own` = true searches the caller's contacts / joined chats and places; false searches other public ones.")]
    public async Task<McpFoundContact[]> SearchContacts(
        [Description("Search text.")] string query,
        [Description("people | groups | places")] string scope = "groups",
        [Description("True for the caller's own contacts/chats/places, false for other public ones.")] bool own = true,
        [Description("Limit to this place.")] string? placeId = null,
        [Description("Max results; capped at 100.")] int limit = DefaultLimit,
        CancellationToken cancellationToken = default)
    {
        var searchScope = scope.ToLower() switch {
            "people" => SearchScope.People,
            "groups" => SearchScope.Groups,
            "places" => SearchScope.Places,
            _ => throw StandardError.Constraint("scope must be people, groups or places."),
        };
        var searchQuery = new ContactSearchQuery {
            Scope = searchScope,
            Own = own,
            Criteria = query,
            PlaceId = PlaceId.ParseNullable(placeId),
            Limit = Math.Clamp(limit, 1, MaxLimit),
        };
        var result = await Search.FindContacts(Session, searchQuery, cancellationToken).ConfigureAwait(false);
        var found = await Task.WhenAll(result.Items.Select(i => Resolve(i, cancellationToken))).ConfigureAwait(false);
        return found.Where(f => f is not null).Cast<McpFoundContact>().ToArray();
    }

    // Private methods

    private async Task<McpFoundContact?> Resolve(FoundContact found, CancellationToken cancellationToken)
    {
        var contactId = found.ContactId;
        if (contactId.Kind == ContactKind.User) {
            var userId = ((PeerChatId)contactId.ChatId).AnotherUserId(contactId.OwnerId);
            var account = await Accounts.Get(Session, userId, cancellationToken).ConfigureAwait(false);
            return account is null ? null : new McpFoundContact("user", account.Id.Value, account.Avatar.Name);
        }
        if (contactId.Kind == ContactKind.Place) {
            var placeId = ((PlaceChatId)contactId.ChatId).PlaceId;
            var place = await Places.Get(Session, placeId, cancellationToken).ConfigureAwait(false);
            return place is null ? null : new McpFoundContact("place", place.Id.Value, place.Title);
        }

        var chat = await Chats.Get(Session, contactId.ChatId, cancellationToken).ConfigureAwait(false);
        return chat is null ? null : new McpFoundContact("chat", chat.Id.Value, chat.Title);
    }
}
