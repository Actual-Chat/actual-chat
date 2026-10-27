using System.Buffers.Text;
using System.ComponentModel;
using System.IO.Compression;
using ActualChat.Contacts;
using ActualChat.Mcp.Auth;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace ActualChat.Mcp.Tools;

/// <summary>
/// Change and unread tracking over every chat the caller has: group, peer and place chats plus followed threads.
/// A cursor holds the last message id of each chat, so nothing posted after it can be missed.
/// </summary>
[McpServerToolType]
public sealed class McpChatActivityTools(IServiceProvider services)
{
    private const int MaxWaitSeconds = 55;
    private const int DefaultWaitSeconds = 30;
    private const string CursorPrefix = "c1.";

    private IContacts Contacts { get; } = services.GetRequiredService<IContacts>();
    private ICommander Commander { get; } = services.Commander();
    private McpSessionAccessor SessionAccessor { get; } = services.GetRequiredService<McpSessionAccessor>();

    private Session Session => SessionAccessor.Session;

    [McpServerTool(Name = "wait_for_changes", UseStructuredContent = true)]
    [Description("Waits until any of the caller's chats (group, peer and place chats, and the threads they follow; " +
        "optionally only one place) has messages after `cursor`, then returns those chats and a new cursor. " +
        "Each change carries `afterId` - pass it to list_messages to read what is new; it is null for a chat " +
        "the cursor doesn't know yet. Returns no changes and the same cursor after `timeoutSeconds`. " +
        "An empty `cursor` returns the current cursor at once, with no changes; list_unread_chats also returns one. " +
        "Persist the cursor once you have handled its changes: calling again with an older cursor repeats them.")]
    public async Task<McpWaitForChangesResult> WaitForChanges(
        [Description("The cursor from the previous call; empty = return the current cursor at once.")]
        string? cursor = null,
        [Description("Only the chats and threads of this place.")]
        string? placeId = null,
        [Description("How long to wait for a change; capped at 55.")]
        int timeoutSeconds = DefaultWaitSeconds,
        CancellationToken cancellationToken = default)
    {
        var subset = ParseSubset(placeId);
        var knownLastIds = ParseCursor(cursor);
        var session = Session;
        if (knownLastIds is null) {
            var ranges = await Contacts.ListChatIdRanges(session, subset, cancellationToken).ConfigureAwait(false);
            return new McpWaitForChangesResult([], FormatCursor(ranges));
        }

        var timeout = TimeSpan.FromSeconds(Math.Clamp(timeoutSeconds, 1, MaxWaitSeconds));
        using var timeoutCts = new CancellationTokenSource(timeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
        var waitToken = linkedCts.Token;
        try {
            while (true) {
                var cRanges = await Computed
                    .Capture(() => Contacts.ListChatIdRanges(session, subset, waitToken), waitToken)
                    .ConfigureAwait(false);
                var ranges = cRanges.Value;
                var changes = GetChanges(knownLastIds, ranges);
                if (changes.Length != 0)
                    return new McpWaitForChangesResult(changes, FormatCursor(ranges));

                await cRanges.WhenInvalidated(waitToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested
            && !cancellationToken.IsCancellationRequested) {
            return new McpWaitForChangesResult([], cursor!);
        }
    }

    [McpServerTool(Name = "list_unread_chats", UseStructuredContent = true)]
    [Description("Lists the caller's chats (group, peer and place chats, and the threads they follow; " +
        "optionally only one place) that have messages after the caller's read position. " +
        "`readId` is that position - pass it to list_messages as `afterId`; `unreadCount` is an upper bound, " +
        "since removed messages leave gaps. A chat the caller never joined or opened isn't listed, as in the app. " +
        "Also returns a cursor for wait_for_changes that matches the list, " +
        "so a caller can read the unread messages and then wait for what comes after them.")]
    public Task<McpListUnreadChatsResult> ListUnreadChats(
        [Description("Only the chats and threads of this place.")]
        string? placeId = null,
        CancellationToken cancellationToken = default)
        => GetUnreadChats(Session, ParseSubset(placeId), cancellationToken);

    [McpServerTool(Name = "wait_for_unread", UseStructuredContent = true)]
    [Description("Waits until any of the caller's chats (as in list_unread_chats) has unread messages, " +
        "then returns the same result as list_unread_chats. Returns at once while any chat is unread, " +
        "so mark_read each chat once you have handled it. Do that before you reply: posting moves the read " +
        "position over your own message only if you had read everything before it. " +
        "Returns no chats after `timeoutSeconds`.")]
    public async Task<McpListUnreadChatsResult> WaitForUnread(
        [Description("Only the chats and threads of this place.")]
        string? placeId = null,
        [Description("How long to wait for an unread message; capped at 55.")]
        int timeoutSeconds = DefaultWaitSeconds,
        CancellationToken cancellationToken = default)
    {
        var subset = ParseSubset(placeId);
        var session = Session;
        var timeout = TimeSpan.FromSeconds(Math.Clamp(timeoutSeconds, 1, MaxWaitSeconds));
        using var timeoutCts = new CancellationTokenSource(timeout);
        using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);
        var waitToken = linkedCts.Token;
        try {
            while (true) {
                // Read before the unread list for the same reason as in list_unread_chats
                var ranges = await Contacts.ListChatIdRanges(session, subset, waitToken).ConfigureAwait(false);
                var cUnreadChats = await Computed
                    .Capture(() => Contacts.ListUnreadChats(session, subset, waitToken), waitToken)
                    .ConfigureAwait(false);
                if (cUnreadChats.Value.Count != 0)
                    return ToResult(cUnreadChats.Value, ranges);

                await cUnreadChats.WhenInvalidated(waitToken).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) when (timeoutCts.IsCancellationRequested
            && !cancellationToken.IsCancellationRequested) {
            return await GetUnreadChats(session, subset, cancellationToken).ConfigureAwait(false);
        }
    }

    [McpServerTool(Name = "mark_read", UseStructuredContent = true)]
    [Description("Moves the caller's read position in a chat forward to `lastId`, the last message id handled; " +
        "a position never moves back. It drives list_unread_chats, wait_for_unread and the unread counts " +
        "the caller's apps show.")]
    public async Task MarkRead(
        [Description("The chat id.")] string chatId,
        [Description("The id of the last message read.")] long lastId,
        CancellationToken cancellationToken = default)
    {
        var command = new ChatPositions_Set {
            Session = Session,
            ChatId = ChatId.Parse(chatId),
            Kind = ChatPositionKind.Read,
            Position = new ChatPosition(lastId),
        };
        await Commander.Call(command, cancellationToken).ConfigureAwait(false);
    }

    // Private methods

    private async Task<McpListUnreadChatsResult> GetUnreadChats(
        Session session, ContactSubset subset, CancellationToken cancellationToken)
    {
        // The cursor is read first, so a message that lands in between is repeated by wait_for_changes, not missed
        var ranges = await Contacts.ListChatIdRanges(session, subset, cancellationToken).ConfigureAwait(false);
        var unreadChats = await Contacts.ListUnreadChats(session, subset, cancellationToken).ConfigureAwait(false);
        return ToResult(unreadChats, ranges);
    }

    private static McpListUnreadChatsResult ToResult(ApiArray<UnreadChat> unreadChats, ApiArray<ChatIdRange> ranges)
    {
        var chats = unreadChats
            .Select(c => new McpUnreadChat(c.ChatId.Value, c.ReadEntryLid, c.LastEntryLid, c.UnreadCount))
            .ToArray();
        return new McpListUnreadChatsResult(chats, FormatCursor(ranges));
    }

    private static McpChatChange[] GetChanges(Dictionary<ChatId, long> knownLastIds, ApiArray<ChatIdRange> ranges)
    {
        var changes = new List<McpChatChange>();
        foreach (var range in ranges) {
            var chatId = range.ChatId;
            var lastId = range.LastEntryLid;
            if (knownLastIds.TryGetValue(chatId, out var knownLastId)) {
                if (lastId > knownLastId)
                    changes.Add(new McpChatChange(chatId.Value, knownLastId, lastId));
            }
            else if (!range.IdRange.IsEmptyOrNegative)
                changes.Add(new McpChatChange(chatId.Value, null, lastId));
        }
        return changes.ToArray();
    }

    private static ContactSubset ParseSubset(string? placeId)
    {
        if (placeId.IsNullOrEmpty())
            return ContactSubset.All();
        if (!PlaceId.TryParse(placeId, out var result))
            throw new McpException($"Invalid placeId: {placeId}");

        return ContactSubset.Place(result);
    }

    private static string FormatCursor(ApiArray<ChatIdRange> ranges)
    {
        // Sorted, so an unchanged set of chats always yields the same cursor
        var lines = ranges
            .Select(x => $"{x.ChatId.Value} {x.LastEntryLid}")
            .Order();
        var text = string.Join('\n', lines);
        using var buffer = new MemoryStream();
        using (var zlib = new ZLibStream(buffer, CompressionLevel.SmallestSize))
            zlib.Write(System.Text.Encoding.UTF8.GetBytes(text));
        return CursorPrefix + Base64Url.EncodeToString(buffer.ToArray());
    }

    private static Dictionary<ChatId, long>? ParseCursor(string? cursor)
    {
        if (cursor.IsNullOrEmpty())
            return null;

        try {
            if (!cursor.StartsWith(CursorPrefix))
                throw new FormatException();

            var bytes = Base64Url.DecodeFromChars(cursor.AsSpan(CursorPrefix.Length));
            using var zlib = new ZLibStream(new MemoryStream(bytes), CompressionMode.Decompress);
            using var reader = new StreamReader(zlib, System.Text.Encoding.UTF8);
            var result = new Dictionary<ChatId, long>();
            while (reader.ReadLine() is { } line) {
                var separatorIndex = line.LastIndexOf(' ');
                var chatId = ChatId.Parse(line[..separatorIndex]);
                result[chatId] = long.Parse(line.AsSpan(separatorIndex + 1));
            }
            return result;
        }
        catch (Exception e) when (e is not OperationCanceledException) {
            // Only an McpException's message reaches the caller
            throw new McpException("Invalid cursor: pass the one a previous call returned, or none.");
        }
    }
}
