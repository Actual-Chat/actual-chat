using MemoryPack;

namespace ActualChat.Chat;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
[method: MemoryPackConstructor, JsonConstructor]
public sealed partial record TextEntry(
    [property: DataMember(Order = 0), MemoryPackOrder(0)] long LocalId,
    [property: DataMember(Order = 1), MemoryPackOrder(1)] string Content,
    [property: DataMember(Order = 2), MemoryPackOrder(2)] AuthorId AuthorId,
    [property: DataMember(Order = 3), MemoryPackOrder(3)] Moment BeginsAt,
    [property: DataMember(Order = 4), MemoryPackOrder(4)] Moment? EndsAt,
    [property: DataMember(Order = 5), MemoryPackOrder(5)] bool IsTranscript,
    [property: DataMember(Order = 6), MemoryPackOrder(6)] long? RepliedEntryLid,
    [property: DataMember(Order = 7), MemoryPackOrder(7)] bool HasAttachments)
{
    public static Comparer<TextEntry> LocalIdComparer { get; } = Comparer<TextEntry>.Create((a, b) => a.LocalId.CompareTo(b.LocalId));

    public TextEntry(ChatEntry chatEntry)
        : this(
            chatEntry.LocalId,
            chatEntry.Content,
            chatEntry.AuthorId,
            chatEntry.BeginsAt,
            chatEntry.EndsAt,
            chatEntry.HasAudioEntry,
            chatEntry.RepliedEntryLid,
            chatEntry.Attachments.Length > 0)
    { }
}
