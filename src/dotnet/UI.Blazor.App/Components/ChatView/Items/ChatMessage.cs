namespace ActualChat.UI.Blazor.App.Components;

[ParameterComparer(typeof(ByValueParameterComparer))]
public abstract class ChatMessage(long id) : IVirtualListItem, IEquatable<ChatMessage>
{
    private Symbol? _key;

    public string Key => _key ??= GetKey();

    public long Id { get; } = id;

    public ChatMessageReplacementKind ReplacementKind { get; init; }
    public DateOnly Date { get; init; }
    public ChatMessageFlags Flags { get; init; }
    public ChatMessage? PreviousMessage { get; init; }
    public ChatMessage? NextMessage { get; set; }
    public Conversation? Conversation { get; init; }
    public bool ShowIndexDocId { get; init; }
    public string IndexDocId { get; init; } = "";
    public virtual bool IsGroup => false;

    public bool IsReplacement
        => ReplacementKind != ChatMessageReplacementKind.None;

    public override string ToString()
        => $"(#{Key})";

    private Symbol GetKey()
        => Id.Format() + ReplacementKind.GetKeySuffix();

    // Equality

    public override bool Equals(object? obj)
        => ReferenceEquals(this, obj) || (obj is ChatMessage other && Equals(other));

    public abstract bool Equals(ChatMessage? other);

    public abstract override int GetHashCode();

    public static bool operator ==(ChatMessage? left, ChatMessage? right) => Equals(left, right);
    public static bool operator !=(ChatMessage? left, ChatMessage? right) => !Equals(left, right);

    // Static helpers

    public static ChatMessage Welcome(ChatId chatId, bool isBot)
    {
        var chatEntryId = new ChatEntryId(chatId, ChatEntryKind.Text, 0L, AssumeValid.Option);
        var chatEntry = new ChatEntry(chatEntryId);
        return isBot
            ? new ChatEntryMessage(chatEntry) { ReplacementKind = ChatMessageReplacementKind.SearchWelcomeBlock }
            : new ChatEntryMessage(chatEntry) { ReplacementKind = ChatMessageReplacementKind.WelcomeBlock };
    }
}
