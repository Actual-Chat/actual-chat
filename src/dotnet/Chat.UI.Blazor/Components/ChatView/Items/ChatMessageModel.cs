using ActualChat.Media;
using ActualChat.UI.Blazor.Services;

namespace ActualChat.Chat.UI.Blazor.Components;

[ParameterComparer(typeof(ByValueParameterComparer))]
public sealed class ChatMessageModel(ChatEntry entry) : IVirtualListItem, IEquatable<ChatMessageModel>
{
    private static readonly TimeSpan BlockSplitPauseDuration = TimeSpan.FromSeconds(120);
    private Symbol? _key;

    public Symbol Key => _key ??= GetKey();

    public ChatEntry Entry { get; } = entry;
    public bool IsBlockStart { get; init; }
    public bool IsBlockEnd { get; init; }
    public bool IsForwardBlockStart { get; init; }
    public bool HasEntryKindSign { get; init; }
    public int CountAs { get; init; } = 1;
    public ChatMessageReplacementKind ReplacementKind { get; init; }
    public DateOnly DateLineDate { get; init; }
    public Media.LinkPreview? LinkPreview { get; init; }

    public bool ShowLinkPreview
        => LinkPreview is { IsEmpty: false } && Entry.LinkPreviewMode != LinkPreviewMode.Dismiss;

    public override string ToString()
        => $"(#{Key} -> {Entry})";

    private Symbol GetKey()
        => Entry.LocalId.Format() + ReplacementKind.GetKeySuffix();

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
            && IsBlockStart == other.IsBlockStart
            && IsBlockEnd == other.IsBlockEnd
            && HasEntryKindSign == other.HasEntryKindSign
            && DateLineDate == other.DateLineDate
            && ReplacementKind == other.ReplacementKind
            && Entry.Attachments.SequenceEqual(other.Entry.Attachments);
    }

    public override int GetHashCode()
        => HashCode.Combine(
            Entry,
            IsBlockStart,
            IsBlockEnd,
            HasEntryKindSign,
            DateLineDate,
            ReplacementKind,
            Entry.Attachments.Count); // Fine to have something that's fast here

    public static bool operator ==(ChatMessageModel? left, ChatMessageModel? right) => Equals(left, right);
    public static bool operator !=(ChatMessageModel? left, ChatMessageModel? right) => !Equals(left, right);

    // Static helpers

    public static List<ChatMessageModel> FromEmpty(ChatId chatId)
    {
        var chatEntryId = new ChatEntryId(chatId, ChatEntryKind.Text, 0L, AssumeValid.Option);
        var chatEntry = new ChatEntry(chatEntryId);
        var chatMessageModel = new ChatMessageModel(chatEntry) {
            IsBlockStart = true,
            IsBlockEnd = true,
            ReplacementKind = ChatMessageReplacementKind.WelcomeBlock,
        };
        return new List<ChatMessageModel>() { chatMessageModel };
    }

    public static List<ChatMessageModel> FromEntries(
        List<ChatEntry> chatEntries,
        IReadOnlyCollection<ChatMessageModel> oldItems,
        long lastReadEntryId,
        bool hasVeryFirstItem,
        TimeZoneConverter timeZoneConverter,
        IReadOnlyDictionary<Symbol, Media.LinkPreview> linkPreviews)
    {
        var result = new List<ChatMessageModel>(chatEntries.Count);
        var isBlockStart = true;
        var lastDate = default(DateOnly);
        var isPrevUnread = true;
        var isPrevAudio = (bool?)false;
        var isPrevForward = false;
        var prevForwardChatId = ChatId.None;
        var oldItemsMap = oldItems.ToDictionary(i => i.Key, i => i);
        var addWelcomeBlock = hasVeryFirstItem;
        for (var index = 0; index < chatEntries.Count; index++) {
            var entry = chatEntries[index];
            var isLastEntry = index == chatEntries.Count - 1;
            var nextEntry = isLastEntry ? null : chatEntries[index + 1];

            var date = DateOnly.FromDateTime(timeZoneConverter.ToLocalTime(entry.BeginsAt));
            var isBlockEnd = ShouldSplit(entry, nextEntry);
            var isForward = !entry.ForwardedAuthorId.IsNone;
            var isForwardFromOtherChat = prevForwardChatId != entry.ForwardedChatEntryId.ChatId;
            var isForwardBlockStart = (isBlockStart && isForward) || (isForward && (!isPrevForward || isForwardFromOtherChat));
            var isUnread = entry.LocalId > lastReadEntryId;
            var isAudio = entry.AudioEntryId != null || entry.IsStreaming;
            var isEntryKindChanged = isPrevAudio is not { } vIsPrevAudio || (vIsPrevAudio ^ isAudio);
            var addDateLine = date != lastDate && (hasVeryFirstItem || index != 0);
            if (addWelcomeBlock) {
                result.Add(new ChatMessageModel(entry) {
                    ReplacementKind = ChatMessageReplacementKind.WelcomeBlock,
                });
                addWelcomeBlock = false;
            }
            if (isUnread && !isPrevUnread)
                AddItem(new ChatMessageModel(entry) {
                    ReplacementKind = ChatMessageReplacementKind.NewMessagesLine,
                });
            if (addDateLine)
                AddItem(new ChatMessageModel(entry) {
                    ReplacementKind = ChatMessageReplacementKind.DateLine,
                    DateLineDate = date,
                });

            {
                var item = new ChatMessageModel(entry) {
                    IsBlockStart = isBlockStart,
                    IsBlockEnd = isBlockEnd,
                    HasEntryKindSign = isEntryKindChanged || (isBlockStart && isAudio),
                    IsForwardBlockStart = isForwardBlockStart,
                    LinkPreview = linkPreviews.GetValueOrDefault(entry.LinkPreviewId)
                };
                AddItem(item);
            }

            isPrevUnread = isUnread;
            isBlockStart = isBlockEnd;
            lastDate = date;
            isPrevAudio = isAudio;
            isPrevForward = isForward;
            prevForwardChatId = entry.ForwardedChatEntryId.ChatId;
        }

        return result;

        void AddItem(ChatMessageModel item) {
            var oldItem = oldItemsMap.GetValueOrDefault(item.Key);
            if (oldItem != null && oldItem.Equals(item))
                item = oldItem;
            result.Add(item);
        }

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
