using MemoryPack;

namespace ActualChat.UI.Blazor.App.Services;

[StructLayout(LayoutKind.Auto)]
[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public partial record ActiveChat(
    [property: DataMember, MemoryPackOrder(0)] ChatId ChatId,
    [property: DataMember, MemoryPackOrder(1)] bool IsListening = false,
    [property: DataMember, MemoryPackOrder(2)] bool IsRecording = false,
    [property: DataMember, MemoryPackOrder(3)] Moment Recency = default, // CPU time
    [property: DataMember, MemoryPackOrder(4)] Moment ListeningRecency = default // CPU time
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
    public virtual bool Equals(ActiveChat? other) => Equals(ChatId, other?.ChatId);
    public override int GetHashCode() => ChatId.GetHashCode();
}
