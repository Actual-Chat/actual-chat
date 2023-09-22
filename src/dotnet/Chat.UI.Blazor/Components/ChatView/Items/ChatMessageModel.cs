using ActualChat.UI.Blazor.Services;

namespace ActualChat.Chat.UI.Blazor.Components;

[ParameterComparer(typeof(ByValueParameterComparer))]
public sealed class ChatMessageModel : IVirtualListItem, IEquatable<ChatMessageModel>
{
    private static readonly TimeSpan BlockSplitPauseDuration = TimeSpan.FromSeconds(120);
    private Symbol? _key;

    public Symbol Key => _key ??= GetKey();

    public ChatEntry Entry { get; }
    public DateOnly? DateLine { get; init; }
    public bool IsBlockStart { get; init; }
    public bool IsBlockEnd { get; init; }
    public bool IsForwardBlockStart { get; init; }
    public int CountAs { get; init; } = 1;
    public bool IsFirstUnreadSeparator { get; init; }
    public bool ShowEntryKind { get; init; }
    public bool IsWelcome { get; set; }

    public ChatMessageModel(ChatEntry entry)
        => Entry = entry;

    public override string ToString()
        => $"(#{Key} -> {Entry})";

    private Symbol GetKey()
    {
        var key = Entry.LocalId.Format();
        if (DateLine != null)
            return $"{key}-date-{DateLine.Value.ToString("yyyyMMdd", CultureInfo.InvariantCulture)}";
        if (IsWelcome)
            return $"{key}-welcome-message";

        if (IsFirstUnreadSeparator)
            return $"{key}-new-messages";

        return key;
    }

    // Equality

    public override bool Equals(object? obj)
        => ReferenceEquals(this, obj) || (obj is ChatMessageModel other && Equals(other));

    public bool Equals(ChatMessageModel? other)
    {
        if (ReferenceEquals(null, other))
            return false;
        if (ReferenceEquals(this, other))
            return true;

        return Entry.VersionEquals(other.Entry)
            && Nullable.Equals(DateLine, other.DateLine)
            && IsBlockStart == other.IsBlockStart
            && IsBlockEnd == other.IsBlockEnd
            && IsFirstUnreadSeparator == other.IsFirstUnreadSeparator
            && ShowEntryKind == other.ShowEntryKind
            && Entry.Attachments.SequenceEqual(other.Entry.Attachments);
    }

    public override int GetHashCode()
        => HashCode.Combine(Entry, DateLine, IsBlockStart, IsBlockEnd);

    public static bool operator ==(ChatMessageModel? left, ChatMessageModel? right) => Equals(left, right);
    public static bool operator !=(ChatMessageModel? left, ChatMessageModel? right) => !Equals(left, right);

    // Static helpers

    public static List<ChatMessageModel> FromEntries(
        List<ChatEntry> chatEntries,
        IReadOnlyCollection<ChatMessageModel> oldItems,
        long? lastReadEntryId,
        bool hasVeryFirstItem,
        bool addWelcomeMessage,
        TimeZoneConverter timeZoneConverter)
    {
        var result = new List<ChatMessageModel>(chatEntries.Count);
        var isBlockStart = true;
        var lastDate = default(DateOnly);
        var isPrevUnread = true;
        var isPrevAudio = (bool?)false;
        var isPrevForward = false;
        var prevForwardChatId = ChatId.None;
        var oldItemsMap = oldItems.ToDictionary(i => i.Key, i => i);
        for (var index = 0; index < chatEntries.Count; index++) {
            var entry = chatEntries[index];
            var isLastEntry = index == chatEntries.Count - 1;
            var nextEntry = isLastEntry ? null : chatEntries[index + 1];

            var date = DateOnly.FromDateTime(timeZoneConverter.ToLocalTime(entry.BeginsAt));
            var hasDateLine = date != lastDate && (hasVeryFirstItem || index != 0);
            var isBlockEnd = ShouldSplit(entry, nextEntry);
            var isForward = !entry.ForwardedAuthorId.IsNone;
            var forwardFromOtherChat = prevForwardChatId != entry.ForwardedChatEntryId.ChatId;
            var isForwardBlockStart = (isBlockStart && isForward) || (isForward && (!isPrevForward || forwardFromOtherChat));
            var isUnread = entry.LocalId > (lastReadEntryId ?? 0);
            var isAudio = entry.AudioEntryId != null || entry.IsStreaming;
            var isEntryKindChanged = isPrevAudio is not { } vIsPrevAudio || (vIsPrevAudio ^ isAudio);
            if (hasDateLine) {
                var item = new ChatMessageModel(entry) {
                    DateLine = date,
                };
                var oldItem = oldItemsMap.GetValueOrDefault(item.Key);
                if (oldItem != null && oldItem.Entry.Version == item.Entry.Version)
                    result.Add(oldItem);
                else
                    result.Add(item);
                if (addWelcomeMessage) {
                    result.Add(new ChatMessageModel(entry) {
                        IsWelcome = true
                    });
                    addWelcomeMessage = false;
                }
            }
            if (isUnread && !isPrevUnread) {
                var item = new ChatMessageModel(entry) {
                    IsFirstUnreadSeparator = true,
                };
                var oldItem = oldItemsMap.GetValueOrDefault(item.Key);
                if (oldItem != null && oldItem.Entry.Version == item.Entry.Version)
                    result.Add(oldItem);
                else
                    result.Add(item);
            }

            {
                var item = new ChatMessageModel(entry) {
                    IsBlockStart = isBlockStart,
                    IsBlockEnd = isBlockEnd,
                    ShowEntryKind = isEntryKindChanged || (isBlockStart && isAudio),
                    IsForwardBlockStart = isForwardBlockStart,
                };
                var oldItem = oldItemsMap.GetValueOrDefault(item.Key);
                if (oldItem != null && oldItem.Equals(item))
                    result.Add(oldItem);
                else
                    result.Add(item);
            }

            isPrevUnread = isUnread;
            isBlockStart = isBlockEnd;
            lastDate = date;
            isPrevAudio = isAudio;
            isPrevForward = isForward;
            prevForwardChatId = entry.ForwardedChatEntryId.ChatId;
        }

        return result;

        bool ShouldSplit(
            ChatEntry entry,
            ChatEntry? nextEntry)
        {
            if (nextEntry == null)
                return false;
            if (entry.AuthorId != nextEntry.AuthorId)
                return true;

            var prevEndsAt = entry.EndsAt ?? entry.BeginsAt;
            return nextEntry.BeginsAt - prevEndsAt >= BlockSplitPauseDuration;
        }
    }
}
