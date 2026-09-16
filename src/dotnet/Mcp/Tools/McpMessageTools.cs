using System.ComponentModel;
using ActualChat.Mcp.Auth;
using ActualChat.Notifications;
using ModelContextProtocol.Server;

namespace ActualChat.Mcp.Tools;

[McpServerToolType]
public sealed class McpMessageTools(IServiceProvider services)
{
    private const int DefaultLimit = 256;
    private const int MaxLimit = 1024;

    private IChats Chats { get; } = services.GetRequiredService<IChats>();
    private IAuthors Authors { get; } = services.GetRequiredService<IAuthors>();
    private IReactions Reactions { get; } = services.GetRequiredService<IReactions>();
    private UrlMapper UrlMapper { get; } = services.GetRequiredService<UrlMapper>();
    private ICommander Commander { get; } = services.Commander();
    private McpSessionAccessor SessionAccessor { get; } = services.GetRequiredService<McpSessionAccessor>();

    private Session Session => SessionAccessor.Session;

    [McpServerTool(Name = "post_message", UseStructuredContent = true)]
    [Description("Post a new text message to a chat, optionally as a reply and/or with uploaded attachments. " +
        "Returns the new entry's local id (LID).")]
    public async Task<long> PostMessage(
        [Description("The chat id (e.g. group, peer, or place chat id).")] string chatId,
        [Description("The message text.")] string text,
        [Description("LID of the message this one replies to.")] long? replyToId = null,
        [Description("Media ids from finish_upload to attach, in order.")] string[]? attachmentMediaIds = null,
        CancellationToken cancellationToken = default)
    {
        var parsedChatId = ChatId.Parse(chatId);
        var attachments = (attachmentMediaIds ?? [])
            .Select((id, index) => new ChatEntryAttachment { MediaId = MediaId.Parse(id), Index = index })
            .ToArray();
        var command = new Chats_UpsertEntry {
            Session = Session,
            ChatId = parsedChatId,
            LocalId = null,
            Text = text,
            RepliedEntryLid = replyToId is null ? default : replyToId,
            Attachments = attachments,
        };
        var entry = await Commander.Call(command, cancellationToken).ConfigureAwait(false);
        return entry.LocalId;
    }

    [McpServerTool(Name = "edit_message", UseStructuredContent = true)]
    [Description("Edit an existing text message by its local id.")]
    public async Task EditMessage(
        [Description("The chat id.")] string chatId,
        [Description("The message local id (LID).")] long entryId,
        [Description("The new message text.")] string text,
        CancellationToken cancellationToken)
    {
        var parsedChatId = ChatId.Parse(chatId);
        var command = new Chats_UpsertEntry {
            Session = Session,
            ChatId = parsedChatId,
            LocalId = entryId,
            Text = text,
        };
        await Commander.Call(command, cancellationToken).ConfigureAwait(false);
    }

    [McpServerTool(Name = "remove_message", UseStructuredContent = true)]
    [Description("Soft-remove a message by its local id.")]
    public async Task RemoveMessage(
        [Description("The chat id.")] string chatId,
        [Description("The message local id (LID).")] long entryId,
        CancellationToken cancellationToken)
    {
        var parsedChatId = ChatId.Parse(chatId);
        var command = new Chats_RemoveEntry { Session = Session, ChatId = parsedChatId, LocalId = entryId };
        await Commander.Call(command, cancellationToken).ConfigureAwait(false);
    }

    [McpServerTool(Name = "get_id_range", UseStructuredContent = true)]
    [Description("Returns the inclusive LID range {firstId, lastId} for the chat. " +
        "Removed entries are skipped, so there may be gaps in the LID sequence returned by list_messages.")]
    public async Task<McpIdRange<long>> GetIdRange(
        [Description("The chat id.")] string chatId,
        CancellationToken cancellationToken)
    {
        var parsedChatId = ChatId.Parse(chatId);
        var range = await Chats.GetIdRange(Session, parsedChatId, cancellationToken).ConfigureAwait(false);
        return range.ToMcpModel();
    }

    [McpServerTool(Name = "list_messages", UseStructuredContent = true)]
    [Description("Lists up to `limit` messages with LID > `afterId`. " +
        "Returns the inclusive range of the messages, the chat's full inclusive range, and the messages themselves. " +
        "Removed entries are skipped (their LIDs appear as gaps); system and streaming entries are included. " +
        "Each message carries its attachments (images/files) with download, preview, and thumbnail URLs. " +
        "If `afterId` is null, starts from the beginning of the chat. `limit` is capped at 1024.")]
    public async Task<McpListMessagesResult> ListMessages(
        [Description("The chat id.")] string chatId,
        [Description("Return messages with id > afterId. Use null to start from the beginning.")] long? afterId = null,
        [Description("Max messages to return; capped at 1024.")] int limit = DefaultLimit,
        CancellationToken cancellationToken = default)
    {
        var parsedChatId = ChatId.Parse(chatId);
        limit = Math.Clamp(limit, 1, MaxLimit);

        var rawFullRange = await Chats.GetIdRange(Session, parsedChatId, cancellationToken).ConfigureAwait(false);
        var fullRange = rawFullRange.ToMcpModel();

        if (rawFullRange.IsEmptyOrNegative)
            return new McpListMessagesResult(
                new McpIdRange<long>(fullRange.FirstId, fullRange.FirstId - 1), fullRange, []);

        var startLid = (afterId ?? -1) + 1;
        if (startLid < rawFullRange.Start)
            startLid = rawFullRange.Start;
        if (startLid >= rawFullRange.End)
            return new McpListMessagesResult(new McpIdRange<long>(startLid, startLid - 1), fullRange, []);

        var entryIdTiles = Constants.Chat.EntryIdTiles;
        var collected = new List<ChatEntry>(limit);
        var tile = entryIdTiles.GetTile(startLid);
        while (collected.Count < limit && tile.Start < rawFullRange.End) {
            var chatTile = await Chats
                .GetTile(Session, parsedChatId, tile.Range, cancellationToken)
                .ConfigureAwait(false);
            foreach (var entry in chatTile.Entries) {
                if (entry.LocalId < startLid)
                    continue;
                collected.Add(entry);
                if (collected.Count >= limit)
                    break;
            }
            tile = tile.Next();
        }

        var messages = await ToMcpMessages(parsedChatId, collected, cancellationToken).ConfigureAwait(false);
        var rangeOut = messages.Length == 0
            ? new McpIdRange<long>(startLid, startLid - 1)
            : new McpIdRange<long>(messages[0].Id, messages[^1].Id);
        return new McpListMessagesResult(rangeOut, fullRange, messages);
    }

    [McpServerTool(Name = "pin_message", UseStructuredContent = true)]
    [Description("Pins a message in the chat.")]
    public Task PinMessage(
        [Description("The chat id.")] string chatId,
        [Description("The message local id (LID).")] long entryId,
        CancellationToken cancellationToken)
        => SetPinned(chatId, entryId, true, cancellationToken);

    [McpServerTool(Name = "unpin_message", UseStructuredContent = true)]
    [Description("Unpins a message in the chat.")]
    public Task UnpinMessage(
        [Description("The chat id.")] string chatId,
        [Description("The message local id (LID).")] long entryId,
        CancellationToken cancellationToken)
        => SetPinned(chatId, entryId, false, cancellationToken);

    [McpServerTool(Name = "list_pinned_messages", UseStructuredContent = true)]
    [Description("Lists the chat's pinned messages.")]
    public async Task<McpChatMessage[]> ListPinnedMessages(
        [Description("The chat id.")] string chatId,
        CancellationToken cancellationToken)
    {
        var parsedChatId = ChatId.Parse(chatId);
        var entryIds = await Chats.ListPinnedEntries(Session, parsedChatId, cancellationToken).ConfigureAwait(false);
        var entries = await Task.WhenAll(entryIds.Select(id => Chats.GetEntry(Session, id, cancellationToken).AsTask()))
            .ConfigureAwait(false);
        var existing = entries.Where(e => e is not null).Cast<ChatEntry>().ToList();
        return await ToMcpMessages(parsedChatId, existing, cancellationToken).ConfigureAwait(false);
    }

    [McpServerTool(Name = "react", UseStructuredContent = true)]
    [Description("Toggles the caller's reaction on a message: adds it, or removes it if the same emoji " +
        "is already set. `emoji` is the emoji character, e.g. \"👍\".")]
    public async Task React(
        [Description("The chat id.")] string chatId,
        [Description("The message local id (LID).")] long entryId,
        [Description("The emoji character.")] string emoji,
        CancellationToken cancellationToken)
    {
        var reaction = new Reaction {
            Id = Symbol.Empty,
            AuthorId = null!,
            EntryId = ChatEntryId.New(ChatId.Parse(chatId), entryId),
            Emoji = Emoji.Parse(emoji),
        };
        var command = new Reactions_React { Session = Session, Reaction = reaction };
        await Commander.Call(command, cancellationToken).ConfigureAwait(false);
    }

    [McpServerTool(Name = "list_reactions", UseStructuredContent = true)]
    [Description("Lists reaction summaries on a message: emoji, count and the first reacting author ids.")]
    public async Task<McpReactionSummary[]> ListReactions(
        [Description("The chat id.")] string chatId,
        [Description("The message local id (LID).")] long entryId,
        CancellationToken cancellationToken)
    {
        var parsedEntryId = ChatEntryId.New(ChatId.Parse(chatId), entryId);
        var summaries = await Reactions.ListSummaries(Session, parsedEntryId, cancellationToken).ConfigureAwait(false);
        return summaries
            .Where(s => s.Count > 0)
            .Select(s => new McpReactionSummary(
                s.Emoji.Value,
                s.Count,
                s.FirstAuthorIds.Select(a => a.Value).ToArray()))
            .ToArray();
    }

    [McpServerTool(Name = "notify_members", UseStructuredContent = true)]
    [Description("Rings all chat members (pushes through mute). Only for private chats " +
        "with at most 10 members; use sparingly.")]
    public async Task NotifyMembers(
        [Description("The chat id.")] string chatId,
        CancellationToken cancellationToken)
    {
        var command = new Notifications_NotifyMembers { Session = Session, ChatId = ChatId.Parse(chatId) };
        await Commander.Call(command, cancellationToken).ConfigureAwait(false);
    }

    [McpServerTool(Name = "notify_mentioned", UseStructuredContent = true)]
    [Description("Rings the members mentioned in one of the caller's own messages (pushes through mute). " +
        "Mention members in the text as @u:<userId>.")]
    public async Task NotifyMentioned(
        [Description("The chat id.")] string chatId,
        [Description("The message local id (LID).")] long entryId,
        CancellationToken cancellationToken)
    {
        var command = new Notifications_NotifyMentionedMembers {
            Session = Session,
            ChatEntryId = ChatEntryId.New(ChatId.Parse(chatId), entryId),
        };
        await Commander.Call(command, cancellationToken).ConfigureAwait(false);
    }

    // Private methods

    private async Task SetPinned(string chatId, long entryId, bool mustPin, CancellationToken cancellationToken)
    {
        var command = new Chats_SetPinned {
            Session = Session,
            EntryId = ChatEntryId.New(ChatId.Parse(chatId), entryId),
            MustPin = mustPin,
        };
        await Commander.Call(command, cancellationToken).ConfigureAwait(false);
    }

    private async Task<McpChatMessage[]> ToMcpMessages(
        ChatId chatId, IReadOnlyList<ChatEntry> entries, CancellationToken cancellationToken)
    {
        var distinctAuthorIds = entries.Select(e => e.AuthorId).Distinct().ToArray();
        var authorById = new Dictionary<AuthorId, Author?>(distinctAuthorIds.Length);
        var fetched = await Task.WhenAll(distinctAuthorIds.Select(id =>
            Authors.Get(Session, chatId, id, cancellationToken)))
            .ConfigureAwait(false);
        for (var i = 0; i < distinctAuthorIds.Length; i++)
            authorById[distinctAuthorIds[i]] = fetched[i];

        return entries.Select(e => e.ToMcpModel(authorById, UrlMapper)).ToArray();
    }
}
