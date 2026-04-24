namespace ActualChat.Chat;

/// <summary>
/// Lightweight representation of a text chat entry for streaming and translation.
/// </summary>
[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject]
[method: MemoryPackConstructor, SerializationConstructor, JsonConstructor, Newtonsoft.Json.JsonConstructor]
public sealed partial record ChatEntrySlim(
    [property: DataMember(Order = 0), MemoryPackOrder(0), Key(0)] long LocalId,
    [property: DataMember(Order = 1), MemoryPackOrder(1), Key(1)] string Content,
    [property: DataMember(Order = 2), MemoryPackOrder(2), Key(2)] AuthorId AuthorId,
    [property: DataMember(Order = 3), MemoryPackOrder(3), Key(3)] Moment BeginsAt,
    [property: DataMember(Order = 4), MemoryPackOrder(4), Key(4)] Moment? EndsAt,
    [property: DataMember(Order = 5), MemoryPackOrder(5), Key(5)] bool IsTranscript,
    [property: DataMember(Order = 6), MemoryPackOrder(6), Key(6)] long? RepliedEntryLid,
    [property: DataMember(Order = 7), MemoryPackOrder(7), Key(7)] bool HasAttachments)
{
    public static Comparer<ChatEntrySlim> LocalIdComparer { get; } = Comparer<ChatEntrySlim>.Create((a, b) => a.LocalId.CompareTo(b.LocalId));

    public ChatEntrySlim(ChatEntry chatEntry)
        : this(
            chatEntry.LocalId,
            chatEntry.Content,
            chatEntry.AuthorId,
            chatEntry.BeginsAt,
            chatEntry.EndsAt,
            chatEntry.HasAudio,
            chatEntry.RepliedEntryLid,
            chatEntry.Attachments.Length > 0)
    { }
}
