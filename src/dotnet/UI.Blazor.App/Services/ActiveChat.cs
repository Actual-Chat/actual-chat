using MemoryPack;

namespace ActualChat.UI.Blazor.App.Services;

[StructLayout(LayoutKind.Auto)]
[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public readonly partial record struct ActiveChat(
    [property: DataMember, MemoryPackOrder(0)] ChatId ChatId,
    [property: DataMember, MemoryPackOrder(1)] bool IsListening = false,
    [property: DataMember, MemoryPackOrder(2)] bool IsRecording = false,
    [property: DataMember, MemoryPackOrder(3)] Moment Recency = default, // CPU time
    [property: DataMember, MemoryPackOrder(4)] Moment ListeningRecency = default // CPU time
    ) : ICanBeNone<ActiveChat>
{
    public static ActiveChat None => default;

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackIgnore]
    public bool IsNone => ChatId is null;

    public bool IsSameAs(ActiveChat other)
        => ChatId == other.ChatId
            && Recency == other.Recency
            && ListeningRecency == other.ListeningRecency
            && IsListening == other.IsListening
            && IsRecording == other.IsRecording;

    public static implicit operator ActiveChat(ChatId chatId)
        => new(chatId);

    // Equality is based solely on ChatId property
    public bool Equals(ActiveChat other) => Equals(ChatId, other.ChatId);
    public override int GetHashCode() => ChatId?.GetHashCode() ?? 0;
}
