namespace ActualChat.UI.Blazor.App.Services;

public partial class ChatUI
{
    private static readonly TimeSpan BlockStartTimeGap = TimeSpan.FromSeconds(120);

    // NOTE: Please don't add excessive computed dependencies without real reason - it might rerender whole chat view content
    [ComputeMethod(MinCacheDuration = 30, InvalidationDelay = 0.1)]
    public virtual async Task<VirtualListTile<ChatMessage>> GetTile(
        ChatId chatId,
        Range<long> idRange,
        ChatMessage? prevMessage,
        long lastReadEntryId,
        CancellationToken cancellationToken = default)
    {
        if (idRange.IsEmptyOrNegative)
            throw new ArgumentOutOfRangeException(nameof(idRange));

        var requestedIdRange = prevMessage == null
            ? idRange.MoveStart(-1) // to request previous item of requested range to properly render block star - we will drop it off
            : idRange;
        var tiles = await ChatView.IdTileStack.FirstLayer
            .GetCoveringTiles(requestedIdRange)
            .Select(t => Chats.GetTile(Session, chatId, ChatEntryKind.Text, t.Range, cancellationToken))
            .Collect()
            .ConfigureAwait(false);
        var entries = tiles.SelectMany(t => t.Entries).ToList();
        if (entries.Count == 0)
            return new VirtualListTile<ChatMessage>(idRange);

        var prevEntry = (ChatEntry?)null;
        var prevDate = DateOnly.MinValue;
        var isPrevUnread = false;
        var isPrevAudio = false;
        var hasVeryFirstItem = false;
        if (prevMessage != null) {
            prevEntry = prevMessage.Entry;
            prevDate = DateOnly.FromDateTime(DateTimeConverter.ToLocalTime(prevEntry.BeginsAt));
            isPrevUnread = prevMessage.Flags.HasFlag(ChatMessageFlags.Unread);
            isPrevAudio = prevEntry.AudioEntryId != null || prevEntry.IsStreaming;
            hasVeryFirstItem = prevMessage.ReplacementKind == ChatMessageReplacementKind.WelcomeBlock;
        }

        var messages = new List<ChatMessage>(entries.Count);
        var isWelcomeBlockAdded = false;
        foreach (var entry in entries) {
            var date = DateOnly.FromDateTime(DateTimeConverter.ToLocalTime(entry.BeginsAt));
            var isBlockStart = IsBlockStart(prevEntry, entry);
            var isForward = !entry.ForwardedAuthorId.IsNone;
            var isPrevForward = prevEntry is { ForwardedAuthorId.IsNone: false };
            var isForwardFromOtherChat = prevEntry?.ForwardedAuthorId.ChatId != entry.ForwardedAuthorId.ChatId;
            var isForwardFromOtherAuthor = prevEntry?.ForwardedAuthorId != entry.ForwardedAuthorId;
            var isForwardBlockStart = (isBlockStart && isForward) || (isForward && (!isPrevForward || isForwardFromOtherChat));
            var isForwardAuthorBlockStart = isForwardBlockStart || (isForward && isForwardFromOtherAuthor);
            var isEntryUnread = entry.LocalId > lastReadEntryId;
            var isAudio = entry.AudioEntryId != null;
            var shouldAddToResult = idRange.Contains(entry.LocalId);
            var flags = default(ChatMessageFlags);
            if (isBlockStart)
                flags |= ChatMessageFlags.BlockStart;
            if ((isBlockStart && isAudio) || isPrevAudio ^ isAudio)
                flags |= ChatMessageFlags.HasEntryKindSign;
            if (isForwardBlockStart)
                flags |= ChatMessageFlags.ForwardStart;
            if (isForwardAuthorBlockStart)
                flags |= ChatMessageFlags.ForwardAuthorStart;
            if (isEntryUnread)
                flags |= ChatMessageFlags.Unread;
            if (shouldAddToResult) {
                if (hasVeryFirstItem && !isWelcomeBlockAdded) {
                    var welcomeMessage = new ChatMessage(entry) {
                        ReplacementKind = ChatMessageReplacementKind.WelcomeBlock,
                        PreviousMessage = prevMessage,
                    };
                    messages.Add(welcomeMessage);
                    prevMessage = welcomeMessage;
                    isWelcomeBlockAdded = true;
                }
                if (isEntryUnread && !isPrevUnread) {
                    var newLineMessage = new ChatMessage(entry) {
                        ReplacementKind = ChatMessageReplacementKind.NewMessagesLine,
                        PreviousMessage = prevMessage,
                    };
                    messages.Add(newLineMessage);
                    prevMessage = newLineMessage;
                }
                if (date != prevDate) {
                    var dateLineMessage = new ChatMessage(entry) {
                        ReplacementKind = ChatMessageReplacementKind.DateLine,
                        Date = date,
                        PreviousMessage = prevMessage,
                    };
                    messages.Add(dateLineMessage);
                    prevMessage = dateLineMessage;
                }
                var message = new ChatMessage(entry) {
                    Date = date,
                    Flags = flags,
                    PreviousMessage = prevMessage,
                };
                messages.Add(message);
                prevMessage = message;
            }
            prevEntry = entry;
            prevDate = date;
            isPrevUnread = isEntryUnread;
            isPrevAudio = isAudio;
        }
        return new VirtualListTile<ChatMessage>($"tile:{idRange.Format()}", messages);
    }

    private static bool IsBlockStart(ChatEntry? prevEntry, ChatEntry entry)
    {
        if (prevEntry == null)
            return true;
        if (prevEntry.AuthorId != entry.AuthorId)
            return true;

        var prevEndsAt = prevEntry.EndsAt.Nullable ?? prevEntry.BeginsAt;
        return entry.BeginsAt - prevEndsAt >= BlockStartTimeGap;
    }
}
