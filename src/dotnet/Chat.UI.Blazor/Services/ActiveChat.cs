namespace ActualChat.Chat.UI.Blazor.Services;

[StructLayout(LayoutKind.Auto)]
public readonly record struct ActiveChat(
    ChatId Id,
    bool IsListening = false,
    bool IsRecording = false,
    Moment Recency = default)
{
    public static implicit operator ActiveChat(ChatId id) => new(id);

    // Equality must rely on Id only
    public bool Equals(ActiveChat other)
        => Id.Equals(other.Id);
    public override int GetHashCode()
        => Id.GetHashCode();
}
