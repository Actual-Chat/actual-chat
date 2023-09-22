using MemoryPack;

namespace ActualChat.Chat.UI.Blazor.Services;

[StructLayout(LayoutKind.Auto)]
[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public readonly partial record struct ActiveChat(
    [property: DataMember, MemoryPackOrder(0)] ChatId ChatId,
    [property: DataMember, MemoryPackOrder(1)] bool IsListening = false,
    [property: DataMember, MemoryPackOrder(2)] bool IsRecording = false,
    [property: DataMember, MemoryPackOrder(3)] Moment Recency = default,
    [property: DataMember, MemoryPackOrder(4)] Moment ListeningRecency = default
    ) : ICanBeNone<ActiveChat>
{
    public static ActiveChat None { get; } = default;

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public bool IsNone => ChatId.IsNone;

    public bool IsSameAs(ActiveChat other)
        => ChatId == other.ChatId
            && Recency == other.Recency
            && ListeningRecency == other.ListeningRecency
            && IsListening == other.IsListening
            && IsRecording == other.IsRecording;

    public static implicit operator ActiveChat(ChatId chatId)
        => new(chatId);

    // Equality must rely on Id only
    public bool Equals(ActiveChat other)
        => ChatId.Equals(other.ChatId);
    public override int GetHashCode()
        => ChatId.GetHashCode();
}
