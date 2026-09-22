using System.ComponentModel;
using ActualChat.External;
using ActualChat.Mcp.Auth;
using ActualChat.Notifications;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace ActualChat.Mcp.Tools;

[McpServerToolType]
public sealed class McpNotificationTools(IServiceProvider services)
{
    private INotifications Notifications { get; } = services.GetRequiredService<INotifications>();
    private IChats Chats { get; } = services.GetRequiredService<IChats>();
    private IAuthors Authors { get; } = services.GetRequiredService<IAuthors>();
    private IMarkupParser MarkupParser { get; } = services.GetRequiredService<IMarkupParser>();
    private UrlMapper UrlMapper { get; } = services.GetRequiredService<UrlMapper>();
    private McpSessionAccessor SessionAccessor { get; } = services.GetRequiredService<McpSessionAccessor>();

    private Session Session => SessionAccessor.Session;

    [McpServerTool(Name = "list_notifications", UseStructuredContent = true)]
    [Description("Lists the caller's notification history - mentions, replies, reactions, attention pings, " +
        "thread and conversation pings, invitations and incoming calls addressed to them - oldest first. " +
        "Plain chat traffic is not logged; rows older than 30 days are gone. " +
        "Persist `nextAfterSeq` and pass it back as `afterSeq` on the next call to see only what is new. " +
        "An empty page has a null `nextAfterSeq`: keep the cursor you already have, since omitting " +
        "`afterSeq` restarts the walk from the beginning. " +
        "`limit` is capped at 256.")]
    public async Task<McpListNotificationsResult> ListNotifications(
        [Description("Kinds to include, e.g. [\"mention\", \"reaction\"]; empty = all. " +
            "Valid: mention, reply, reaction, attention, thread, invitation, conversation, incomingcall.")]
        string[]? kinds = null,
        [Description("Cursor: return items after this seq in walk order " +
            "(newer ones by default, older ones when `newestFirst` is set).")]
        long? afterSeq = null,
        [Description("Max items to return; capped at 256.")]
        int limit = Constants.Notification.HistoryDefaultLimit,
        [Description("Newest first instead of oldest first.")]
        bool newestFirst = false,
        CancellationToken cancellationToken = default)
    {
        var query = new NotificationHistoryQuery {
            Kinds = ParseKinds(kinds),
            AfterSeq = afterSeq ?? 0,
            Limit = limit,
            IsNewestFirst = newestFirst,
        };
        var items = await Notifications.ListHistory(Session, query, cancellationToken).ConfigureAwait(false);
        var result = new McpNotification[items.Count];
        for (var i = 0; i < items.Count; i++)
            result[i] = await ToMcpModel(items[i], cancellationToken).ConfigureAwait(false);

        return new McpListNotificationsResult(result, items.IsEmpty ? null : items[^1].Seq);
    }

    // Private methods

    private async Task<McpNotification> ToMcpModel(NotificationHistoryItem item, CancellationToken cancellationToken)
    {
        var message = item.EntryId is { } entryId
            ? await GetMessage(entryId, cancellationToken).ConfigureAwait(false)
            : null;
        return new McpNotification(
            item.Seq,
            item.Kind.ToString().ToLower(),
            item.SentAt.ToMcpMillis(),
            item.ChatId?.Value,
            item.EntryId?.LocalId,
            item.AuthorId?.Value,
            item.Title,
            item.Text,
            message);
    }

    private async Task<ExternalMessage?> GetMessage(ChatEntryId entryId, CancellationToken cancellationToken)
    {
        // Resolved through the session: a chat the caller has since left yields no message body
        var chat = await Chats.Get(Session, entryId.ChatId, cancellationToken).ConfigureAwait(false);
        if (chat is null)
            return null;

        var entry = await Chats.GetEntry(Session, entryId, cancellationToken).ConfigureAwait(false);
        if (entry is null)
            return null;

        var author = await Authors.Get(Session, entryId.ChatId, entry.AuthorId, cancellationToken)
            .ConfigureAwait(false);
        var authorById = new Dictionary<AuthorId, Author?> { [entry.AuthorId] = author };
        return await entry.ToMcpModel(authorById, UrlMapper, MarkupParser, cancellationToken).ConfigureAwait(false);
    }

    private static ApiArray<NotificationKind> ParseKinds(string[]? kinds)
    {
        if (kinds is null || kinds.Length == 0)
            return ApiArray<NotificationKind>.Empty;

        var result = new List<NotificationKind>(kinds.Length);
        foreach (var name in kinds) {
            // Only an McpException's message reaches the caller - the MCP SDK masks every other
            // exception as a generic error, so the rejected kind has to be named here.
            if (!Enum.TryParse<NotificationKind>(name, ignoreCase: true, out var kind))
                throw new McpException($"Unknown notification kind: '{name}'.");
            if (!NotificationHistoryItem.IsLoggedKind(kind))
                throw new McpException($"Notification kind '{name}' is not logged.");

            result.Add(kind);
        }
        return result.ToApiArray();
    }
}
