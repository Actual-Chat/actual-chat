namespace ActualChat.Users;

[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject(true)]
public sealed partial record DigestPreview
{
    [DataMember, MemoryPackOrder(0)]
    public DigestPreviewChat[] Chats { get; init; } = [];
    [DataMember, MemoryPackOrder(1)]
    public int OtherUnreadCount { get; init; }
    [DataMember, MemoryPackOrder(2)]
    public string RenderedHtml { get; init; } = "";
}

[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject(true)]
public sealed partial record DigestPreviewChat
{
    [DataMember, MemoryPackOrder(0)]
    public string ChatId { get; init; } = "";
    [DataMember, MemoryPackOrder(1)]
    public string Name { get; init; } = "";
    [DataMember, MemoryPackOrder(2)]
    public string Link { get; init; } = "";
    [DataMember, MemoryPackOrder(3)]
    public long UnreadCount { get; init; }
    [DataMember, MemoryPackOrder(4)]
    public string[] BulletPoints { get; init; } = [];
}
