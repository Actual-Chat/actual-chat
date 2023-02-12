namespace ActualChat.Chat.UI.Blazor.Services;

[StructLayout(LayoutKind.Auto)]
[DataContract]
public readonly record struct ActiveChat(
    [property: DataMember] ChatId ChatId,
    [property: DataMember] bool IsListening = false,
    [property: DataMember] bool IsRecording = false,
    [property: DataMember] Moment Recency = default,
    [property: DataMember] Moment ListeningRecency = default)
{
    public static implicit operator ActiveChat(ChatId chatId) => new(chatId);

    // Equality must rely on Id only
    public bool Equals(ActiveChat other)
        => ChatId.Equals(other.ChatId);
    public override int GetHashCode()
        => ChatId.GetHashCode();
}
