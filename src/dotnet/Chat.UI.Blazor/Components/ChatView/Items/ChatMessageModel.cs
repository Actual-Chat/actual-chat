using ActualChat.Media;

namespace ActualChat.Chat.UI.Blazor.Components;

[ParameterComparer(typeof(ByValueParameterComparer))]
public sealed class ChatMessageModel(ChatEntry entry) : IVirtualListItem, IEquatable<ChatMessageModel>
{
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
        => LinkPreview is { IsEmpty: false } && Entry.LinkPreviewMode != LinkPreviewMode.None;

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
            && Entry.Attachments.SequenceEqual(other.Entry.Attachments)
            && LinkPreview == other.LinkPreview;
    }

    public override int GetHashCode()
        => HashCode.Combine(
            Entry,
            IsBlockStart,
            IsBlockEnd,
            HasEntryKindSign,
            DateLineDate,
            ReplacementKind,
            Entry.Attachments.Count,
            LinkPreview); // Fine to have something that's fast here

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
}
