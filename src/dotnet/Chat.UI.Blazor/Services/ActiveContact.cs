namespace ActualChat.Chat.UI.Blazor.Services;

[StructLayout(LayoutKind.Auto)]
public readonly record struct ActiveContact(
    ContactId Id,
    bool IsListening = false,
    bool IsRecording = false,
    Moment Recency = default)
{
    public static implicit operator ActiveContact(ContactId id) => new(id);

    // Equality must rely on Id only
    public bool Equals(ActiveContact other)
        => Id.Equals(other.Id);
    public override int GetHashCode()
        => Id.GetHashCode();
}
