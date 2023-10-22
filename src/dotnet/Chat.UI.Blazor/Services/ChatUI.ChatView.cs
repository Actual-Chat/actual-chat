namespace ActualChat.Chat.UI.Blazor.Services;

public partial class ChatUI
{
    private static readonly TimeSpan BlockStartTimeGap = TimeSpan.FromSeconds(120);

    [ComputeMethod(MinCacheDuration = 30, InvalidationDelay = 0.1)]
    public virtual async Task<VirtualListTile<ChatMessageModel>> GetTile(
        ChatId chatId,
        Range<long> idRange,
        ChatMessageModel? prevMessage,
        bool? isUnread,
        long lastReadEntryId,
        CancellationToken cancellationToken = default)
    {
        var tile = await Chats
            .GetTile(Session, chatId, ChatEntryKind.Text, idRange, cancellationToken)
            .ConfigureAwait(false);

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
            isPrevUnread = (prevMessage.Flags & ChatMessageFlags.Unread) != 0;
            isPrevForward = !prevEntry.ForwardedAuthorId.IsNone;
            prevForwardChatId = prevEntry.ForwardedChatEntryId.ChatId;
            isPrevAudio = prevEntry.AudioEntryId != null || prevEntry.IsStreaming;
            hasVeryFirstItem = prevMessage.ReplacementKind == ChatMessageReplacementKind.WelcomeBlock;
        }

        var entries = tile.Entries;
        var messages = new List<ChatMessageModel>(entries.Count);
        var isWelcomeBlockAdded = false;
        for (var index = 0; index < entries.Count; index++) {
            var entry = entries[index];

            var date = DateOnly.FromDateTime(TimeZoneConverter.ToLocalTime(entry.BeginsAt));
            var isForward = !entry.ForwardedAuthorId.IsNone;
            var isBlockStart = IsBlockStart(prevEntry, entry);
            var isForwardFromOtherChat = prevForwardChatId != entry.ForwardedChatEntryId.ChatId;
            var isForwardBlockStart = (isBlockStart && isForward) || (isForward && (!isPrevForward || isForwardFromOtherChat));
            var isEntryUnread = isUnread ?? entry.LocalId > lastReadEntryId;
            var isAudio = entry.AudioEntryId != null || entry.IsStreaming;
            if (hasVeryFirstItem && !isWelcomeBlockAdded) {
                messages.Add(new ChatMessageModel(entry) {
                    ReplacementKind = ChatMessageReplacementKind.WelcomeBlock,
                });
                isWelcomeBlockAdded = true;
            }
            if (isEntryUnread && !isPrevUnread)
                messages.Add(new ChatMessageModel(entry) {
                    ReplacementKind = ChatMessageReplacementKind.NewMessagesLine,
                });
            if (date != prevDate)
                messages.Add(new ChatMessageModel(entry) {
                    ReplacementKind = ChatMessageReplacementKind.DateLine,
                    Date = date,
                });

            var flags = default(ChatMessageFlags);
            if (isBlockStart)
                flags |= ChatMessageFlags.BlockStart;
            if ((isBlockStart && isAudio) || isPrevAudio ^ isAudio)
                flags |= ChatMessageFlags.HasEntryKindSign;
            if (isForwardBlockStart)
                flags |= ChatMessageFlags.ForwardStart;
            var message = new ChatMessageModel(entry) {
                Date = date,
                Flags = flags,
            };
            messages.Add(message);

            prevMessage = message;
            prevEntry = entry;
            prevDate = date;
            isPrevUnread = isEntryUnread;
            isPrevForward = isForward;
            prevForwardChatId = entry.ForwardedChatEntryId.ChatId;
            isPrevAudio = isAudio;
        }

        return new VirtualListTile<ChatMessageModel>(messages);
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
