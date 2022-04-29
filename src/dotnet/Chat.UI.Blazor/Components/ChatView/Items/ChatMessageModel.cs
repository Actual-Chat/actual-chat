namespace ActualChat.Chat.UI.Blazor.Components;

public sealed class ChatMessageModel : IVirtualListItem, IEquatable<ChatMessageModel>
{
    // private static readonly int MaxBlockLength = 1_000;
    // private static readonly int MaxBlockContentLength = 10_000;
    private static readonly TimeSpan BlockSplitPauseDuration = TimeSpan.FromSeconds(120);

    public Symbol Key { get; }
    public ChatEntry Entry { get; }
    public Markup Markup { get; }
    public ImmutableArray<TextEntryAttachment> Attachments { get; }
    public DateOnly? DateLine { get; init; }
    public bool IsBlockStart { get; init; }
    public bool IsBlockEnd { get; init; }
    public bool IsUnread { get; init; }
    public int CountAs { get; init; } = 1;
    public bool IsFirstUnread { get; init; }

    public ChatMessageModel(ChatEntry entry, Markup markup, ImmutableArray<TextEntryAttachment> attachments)
    {
        Entry = entry;
        Markup = markup;
        Attachments = attachments;
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
            && IsFirstUnread == other.IsFirstUnread
            && Attachments.SequenceEqual(other.Attachments);
    }

    public override int GetHashCode()
        => HashCode.Combine(Entry, DateLine, IsBlockStart, IsBlockEnd);

    public static bool operator ==(ChatMessageModel? left, ChatMessageModel? right) => Equals(left, right);
    public static bool operator !=(ChatMessageModel? left, ChatMessageModel? right) => !Equals(left, right);

    // Static helpers

    public static List<ChatMessageModel> FromEntries(
        List<ChatEntry> chatEntries,
        IDictionary<long, ImmutableArray<TextEntryAttachment>> chatEntryAttachments,
        long? lastReadEntryId,
        IMarkupParser markupParser)
    {
        var result = new List<ChatMessageModel>(chatEntries.Count);

        var isBlockStart = true;
        var lastDate = default(DateOnly);
        var blockContentLength = 0;
        var blockLength = 0;

        var isPrevUnread = true;
        for (var index = 0; index < chatEntries.Count; index++) {
            if (isBlockStart) {
                blockContentLength = 0;
                blockLength = 0;
            }
            var entry = chatEntries[index];
            var isLastEntry = index == chatEntries.Count - 1;
            var nextEntry = isLastEntry ? null : chatEntries[index + 1];

            var markup = entry.AudioEntryId == null
                ? markupParser.Parse(entry.Content)
                : new PlayableTextMarkup(entry.Content, entry.TextToTimeMap);
            var date = DateOnly.FromDateTime(entry.BeginsAt.ToDateTime().ToLocalTime());
            var hasDateLine = date != lastDate;
            var isBlockEnd = ShouldSplit(entry, nextEntry);
            if (!chatEntryAttachments.TryGetValue(entry.Id, out var attachments))
                attachments = ImmutableArray<TextEntryAttachment>.Empty;
            var isUnread = entry.Id > (lastReadEntryId ?? 0);
            var model = new ChatMessageModel(entry, markup, attachments) {
                DateLine = hasDateLine ? date : null,
                IsBlockStart = isBlockStart,
                IsBlockEnd = isBlockEnd,
                IsUnread = isUnread,
                IsFirstUnread = isUnread && !isPrevUnread,
            };
            result.Add(model);

            isPrevUnread = isUnread;
            isBlockStart = isBlockEnd;
            blockLength += 1;
            blockContentLength += entry.Content.Length;
            lastDate = date;
        }

        return result;

        bool ShouldSplit(ChatEntry entry, ChatEntry? nextEntry)
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
