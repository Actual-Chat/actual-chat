using ActualChat.UI.Blazor.Services;

namespace ActualChat.Chat.UI.Blazor.Components;

public sealed class ChatMessageModel : IVirtualListItem, IEquatable<ChatMessageModel>
{
    private static readonly TimeSpan BlockSplitPauseDuration = TimeSpan.FromSeconds(120);

    public Symbol Key { get; }
    public ChatEntry Entry { get; }
    public DateOnly? DateLine { get; init; }
    public bool IsBlockStart { get; init; }
    public bool IsBlockEnd { get; init; }
    public bool IsUnread { get; init; }
    public int CountAs { get; init; } = 1;
    public bool IsFirstUnread { get; init; }
    public bool IsQuote { get; init; }
    public bool ShowEntryType { get; init; }

    public ChatMessageModel(ChatEntry entry)
    {
        Entry = entry;
        Key = entry.Id.ToString(CultureInfo.InvariantCulture);
    }

    public override string ToString()
        => $"(#{Key} -> {Entry})";

    // Equality

    public override bool Equals(object? obj)
        => ReferenceEquals(this, obj) || (obj is ChatMessageModel other && Equals(other));

    public bool Equals(ChatMessageModel? other)
    {
        if (ReferenceEquals(null, other))
            return false;
        if (ReferenceEquals(this, other))
            return true;

        return Entry.Equals(other.Entry)
            && Nullable.Equals(DateLine, other.DateLine)
            && IsBlockStart == other.IsBlockStart
            && IsBlockEnd == other.IsBlockEnd
            && IsFirstUnread == other.IsFirstUnread;
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
        bool hasVeryLastItem,
        TimeZoneConverter timeZoneConverter)
    {
        var result = new List<ChatMessageModel>(chatEntries.Count);
        var oldBlockStartIds = oldItems?
            .Where(i => i.IsBlockStart)
            .Select(i => long.Parse(i.Key, CultureInfo.InvariantCulture))
            .ToHashSet();
        var isBlockStart = true;
        var lastDate = default(DateOnly);
        var isPrevUnread = true;
        var isPrevAudio = (bool?)false;
        for (var index = 0; index < chatEntries.Count; index++) {
            var entry = chatEntries[index];
            var isLastEntry = index == chatEntries.Count - 1;
            var nextEntry = isLastEntry ? null : chatEntries[index + 1];

            var date = DateOnly.FromDateTime(timeZoneConverter.ToLocalTime(entry.BeginsAt));
            var hasDateLine = date != lastDate && (hasVeryFirstItem || index != 0);
            var isBlockEnd = ShouldSplit(entry, nextEntry);
            var isUnread = entry.Id > (lastReadEntryId ?? 0);
            var isAudio = entry.AudioEntryId != null || entry.IsStreaming;
            var contentKindChanged = !isPrevAudio.HasValue || isPrevAudio.Value ^ isAudio;
            var model = new ChatMessageModel(entry) {
                DateLine = hasDateLine ? date : null,
                IsBlockStart = isBlockStart,
                IsBlockEnd = isBlockEnd,
                IsUnread = isUnread,
                IsFirstUnread = isUnread && !isPrevUnread,
                ShowEntryType = contentKindChanged
            };
            result.Add(model);

            isPrevUnread = isUnread;
            isBlockStart = isBlockEnd;
            lastDate = date;
            isPrevAudio = isAudio;
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
            if (oldBlockStartIds != null && oldBlockStartIds.Contains(nextEntry.Id))
                return true;

            var prevEndsAt = entry.EndsAt ?? entry.BeginsAt;
            return nextEntry.BeginsAt - prevEndsAt >= BlockSplitPauseDuration;
        }
    }
}
