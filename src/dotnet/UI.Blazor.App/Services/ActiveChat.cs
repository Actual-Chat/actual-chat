namespace ActualChat.UI.Blazor.App.Services;

[DataContract, MessagePackObject]
public sealed partial record ActiveChat(
    [property: DataMember, Key(0)] ChatId ChatId,
    [property: DataMember, Key(1)] bool IsListening = false,
    [property: DataMember, Key(2)] bool IsRecording = false,
    [property: DataMember, Key(3)] Moment Recency = default, // CPU time
    [property: DataMember, Key(4)] Moment ListeningRecency = default // CPU time
    )
{
    public bool IsSameAs(ActiveChat? other)
        => other != null
            && ChatId == other.ChatId
            && Recency == other.Recency
            && ListeningRecency == other.ListeningRecency
            && IsListening == other.IsListening
            && IsRecording == other.IsRecording;

    public static implicit operator ActiveChat(ChatId chatId)
        => new(chatId);

    // Equality is based solely on ChatId property
    public bool Equals(ActiveChat? other) => Equals(ChatId, other?.ChatId);
    public override int GetHashCode() => ChatId.GetHashCode();
}
