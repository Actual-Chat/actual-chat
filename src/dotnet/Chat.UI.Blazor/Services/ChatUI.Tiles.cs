namespace ActualChat.Chat.UI.Blazor.Services;

public partial class ChatUI
{
    private static readonly TimeSpan BlockStartTimeGap = TimeSpan.FromSeconds(120);

    [ComputeMethod(MinCacheDuration = 30, InvalidationDelay = 0.1)]
    public virtual async Task<VirtualListTile<ChatMessage>> GetTile(
        ChatId chatId,
        Range<long> idRange,
        ChatMessage? prevMessage,
        bool? isUnread,
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
        var isPrevForward = false;
        var prevForwardChatId = ChatId.None;
        var isPrevAudio = false;
        var hasVeryFirstItem = false;
        if (prevMessage != null) {
            prevEntry = prevMessage.Entry;
            prevDate = DateOnly.FromDateTime(TimeZoneConverter.ToLocalTime(prevEntry.BeginsAt));
            isPrevUnread = prevMessage.Flags.HasFlag(ChatMessageFlags.Unread);
            isPrevForward = !prevEntry.ForwardedAuthorId.IsNone;
            prevForwardChatId = prevEntry.ForwardedChatEntryId.ChatId;
            isPrevAudio = prevEntry.AudioEntryId != null || prevEntry.IsStreaming;
            hasVeryFirstItem = prevMessage.ReplacementKind == ChatMessageReplacementKind.WelcomeBlock;
        }

        var messages = new List<ChatMessage>(entries.Count);
        var isWelcomeBlockAdded = false;
        foreach (var entry in entries) {
            var date = DateOnly.FromDateTime(TimeZoneConverter.ToLocalTime(entry.BeginsAt));
            var isForward = !entry.ForwardedAuthorId.IsNone;
            var isBlockStart = IsBlockStart(prevEntry, entry);
            var isForwardFromOtherChat = prevForwardChatId != entry.ForwardedChatEntryId.ChatId;
            var isForwardBlockStart = (isBlockStart && isForward) || (isForward && (!isPrevForward || isForwardFromOtherChat));
            var isEntryUnread = isUnread ?? entry.LocalId > lastReadEntryId;
            var isAudio = entry.AudioEntryId != null || entry.IsStreaming;
            var shouldAddToResult = idRange.Contains(entry.LocalId);
            var flags = default(ChatMessageFlags);
            if (isBlockStart)
                flags |= ChatMessageFlags.BlockStart;
            if ((isBlockStart && isAudio) || isPrevAudio ^ isAudio)
                flags |= ChatMessageFlags.HasEntryKindSign;
            if (isForwardBlockStart)
                flags |= ChatMessageFlags.ForwardStart;
            if (isEntryUnread)
                flags |= ChatMessageFlags.Unread;
            if (shouldAddToResult) {
                if (hasVeryFirstItem && !isWelcomeBlockAdded) {
                    messages.Add(new ChatMessage(entry) {
                        ReplacementKind = ChatMessageReplacementKind.WelcomeBlock,
                    });
                    isWelcomeBlockAdded = true;
                }
                if (isEntryUnread && !isPrevUnread)
                    messages.Add(new ChatMessage(entry) {
                        ReplacementKind = ChatMessageReplacementKind.NewMessagesLine,
                    });
                if (date != prevDate)
                    messages.Add(new ChatMessage(entry) {
                        ReplacementKind = ChatMessageReplacementKind.DateLine,
                        Date = date,
                    });
                var message = new ChatMessage(entry) {
                    Date = date,
                    Flags = flags,
                };
                messages.Add(message);
            }
            prevEntry = entry;
            prevDate = date;
            isPrevUnread = isEntryUnread;
            isPrevForward = isForward;
            prevForwardChatId = entry.ForwardedChatEntryId.ChatId;
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

        var prevEndsAt = prevEntry.EndsAt ?? prevEntry.BeginsAt;
        return entry.BeginsAt - prevEndsAt >= BlockStartTimeGap;
    }
}
