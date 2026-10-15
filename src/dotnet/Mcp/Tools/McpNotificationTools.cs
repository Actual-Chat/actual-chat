using System.ComponentModel;
using System.Security.Cryptography;
using ActualChat.External;
using ActualChat.Mcp.Auth;
using ActualChat.Notifications;
using ModelContextProtocol;
using ModelContextProtocol.Server;
using Notification = ActualChat.Notifications.Notification;

namespace ActualChat.Mcp.Tools;

[McpServerToolType]
public sealed class McpNotificationTools(IServiceProvider services)
{
    private const int MaxWaitSeconds = 55;
    private const int DefaultWaitSeconds = 30;

    private INotifications Notifications { get; } = services.GetRequiredService<INotifications>();
    private IChats Chats { get; } = services.GetRequiredService<IChats>();
    private IAuthors Authors { get; } = services.GetRequiredService<IAuthors>();
    private IMarkupParser MarkupParser { get; } = services.GetRequiredService<IMarkupParser>();
    private UrlMapper UrlMapper { get; } = services.GetRequiredService<UrlMapper>();
    private ICommander Commander { get; } = services.Commander();
    private McpSessionAccessor SessionAccessor { get; } = services.GetRequiredService<McpSessionAccessor>();

    private Session Session => SessionAccessor.Session;

    [McpServerTool(Name = "list_notifications", UseStructuredContent = true)]
    [Description("Lists the caller's active notifications - the ones currently on their devices: " +
        "mentions, replies, reactions, attention pings, thread and conversation pings, " +
        "invitations and incoming calls. Reading a chat clears its mentions, so consume them as they arrive " +
        "(see `wait_for_notifications`). `version` identifies the returned set: pass it to " +
        "`wait_for_notifications` to learn when the set changes. Without `chatId` and `placeId` " +
        "everything is returned; a contact is its peer chat, so pass the peer chat id.")]
    public async Task<McpListNotificationsResult> ListNotifications(
        [Description("Only notifications of this chat (a group chat, a place chat or a peer chat).")]
        string? chatId = null,
        [Description("Only notifications of the chats of this place.")]
        string? placeId = null,
        CancellationToken cancellationToken = default)
    {
        var scope = NotificationScope.Parse(chatId, placeId);
        var active = await Notifications.ListActive(Session, cancellationToken).ConfigureAwait(false);
        return await ToResult(scope.Filter(active), cancellationToken).ConfigureAwait(false);
    }

    [McpServerTool(Name = "wait_for_notifications", UseStructuredContent = true)]
    [Description("Waits until the caller's active notifications (optionally limited to one chat or place) " +
        "differ from the set identified by `version`, then returns the new set and its version. " +
        "Returns the unchanged set after `timeoutSeconds` if nothing changed - call it again with the same " +
        "`version`. Persist the last `version` you processed: after a broken connection, call again with it " +
        "to resume; an empty or stale `version` returns the current set at once.")]
    public async Task<McpListNotificationsResult> WaitForNotifications(
        [Description("The `version` of the last set you saw; empty = return the current set at once.")]
        string? version = null,
        [Description("Only notifications of this chat (a group chat, a place chat or a peer chat).")]
        string? chatId = null,
        [Description("Only notifications of the chats of this place.")]
        string? placeId = null,
        [Description("How long to wait for a change; capped at 55.")]
        int timeoutSeconds = DefaultWaitSeconds,
        CancellationToken cancellationToken = default)
    {
        var scope = NotificationScope.Parse(chatId, placeId);
        var session = Session;
        var timeout = TimeSpan.FromSeconds(Math.Clamp(timeoutSeconds, 1, MaxWaitSeconds));
        using var timeoutCts = new CancellationTokenSource(timeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
        var waitToken = linkedCts.Token;

        try {
            while (true) {
                var cActive = await Computed
                    .Capture(() => Notifications.ListActive(session, waitToken), waitToken)
                    .ConfigureAwait(false);
                var items = scope.Filter(cActive.Value);
                if (version != GetVersion(items))
                    return await ToResult(items, cancellationToken).ConfigureAwait(false);

                await cActive.WhenInvalidated(waitToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested
            && !cancellationToken.IsCancellationRequested) {
            // Timed out: report the set as it is now
            var latest = await Notifications.ListActive(session, cancellationToken).ConfigureAwait(false);
            return await ToResult(scope.Filter(latest), cancellationToken).ConfigureAwait(false);
        }
    }

    [McpServerTool(Name = "dismiss_notification", UseStructuredContent = true)]
    [Description("Dismisses one active notification by its `id` from `list_notifications`.")]
    public async Task DismissNotification(
        [Description("The notification id.")] string id,
        CancellationToken cancellationToken = default)
    {
        var command = new Notifications_Dismiss { Session = Session, NotificationId = NotificationId.Parse(id) };
        await Commander.Call(command, cancellationToken).ConfigureAwait(false);
    }

    [McpServerTool(Name = "dismiss_all_notifications", UseStructuredContent = true)]
    [Description("Dismisses all the caller's active notifications.")]
    public async Task DismissAllNotifications(CancellationToken cancellationToken = default)
    {
        var command = new Notifications_DismissAll { Session = Session };
        await Commander.Call(command, cancellationToken).ConfigureAwait(false);
    }

    // Private methods

    private async Task<McpListNotificationsResult> ToResult(
        IReadOnlyList<Notification> items, CancellationToken cancellationToken)
    {
        var result = new McpNotification[items.Count];
        for (var i = 0; i < items.Count; i++)
            result[i] = await ToMcpModel(items[i], cancellationToken).ConfigureAwait(false);

        return new McpListNotificationsResult(result, GetVersion(items));
    }

    private async Task<McpNotification> ToMcpModel(Notification notification, CancellationToken cancellationToken)
    {
        var entryId = notification.GetAnchorEntryId();
        var message = entryId is { } id
            ? await GetMessage(id, cancellationToken).ConfigureAwait(false)
            : null;
        return new McpNotification(
            notification.Id.Value,
            notification.Kind.ToString().ToLower(),
            notification.SentAt.ToMcpMillis(),
            notification.GetChatId()?.Value,
            entryId?.LocalId,
            (notification as ChatNotification)?.AuthorId?.Value,
            notification.Title,
            notification.Text,
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

    private static string GetVersion(IReadOnlyList<Notification> items)
    {
        // A merged notification keeps its id while its text and SentAt change, so those take part too
        var lines = items
            .Select(n => $"{n.Id.Value}|{n.Version}|{n.SentAt.EpochOffset.Ticks}|{n.Title}|{n.Text}")
            .Order();
        var bytes = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(string.Join('\n', lines)));
        return Convert.ToHexStringLower(bytes)[..16];
    }

    // Nested types

    private sealed record NotificationScope(ChatId? ChatId, PlaceId? PlaceId)
    {
        public static NotificationScope Parse(string? chatId, string? placeId)
        {
            try {
                return new NotificationScope(
                    chatId.IsNullOrEmpty() ? null : ActualChat.ChatId.Parse(chatId),
                    placeId.IsNullOrEmpty() ? null : ActualChat.PlaceId.Parse(placeId));
            }
            catch (Exception e) when (e is not OperationCanceledException) {
                // Only an McpException's message reaches the caller
                throw new McpException($"Invalid chatId or placeId: {e.Message}");
            }
        }

        public IReadOnlyList<Notification> Filter(ApiArray<Notification> active)
            => active.Where(Matches).ToList();

        private bool Matches(Notification notification)
        {
            if (ChatId is null && PlaceId is null)
                return true;

            var notificationChatId = notification.GetChatId();
            if (notificationChatId is null)
                return false;
            if (ChatId is { } chatId && notificationChatId != chatId)
                return false;
            if (PlaceId is { } placeId)
                return notificationChatId is PlaceChatId placeChatId && placeChatId.PlaceId == placeId;

            return true;
        }
    }
}
