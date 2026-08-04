namespace ActualChat.Users;

[DataContract, MessagePackObject]
public sealed partial record DigestPreview
{
    [DataMember, Key(0)]
    public DigestPreviewChat[] Chats { get; init; } = [];
    [DataMember, Key(1)]
    public int OtherUnreadCount { get; init; }
    [DataMember, Key(2)]
    public string RenderedHtml { get; init; } = "";
}

[DataContract, MessagePackObject]
public sealed partial record DigestPreviewChat
{
    [DataMember, Key(0)]
    public string ChatId { get; init; } = "";
    [DataMember, Key(1)]
    public string Name { get; init; } = "";
    [DataMember, Key(2)]
    public string Link { get; init; } = "";
    [DataMember, Key(3)]
    public long UnreadCount { get; init; }
    [DataMember, Key(4)]
    public string[] BulletPoints { get; init; } = [];
}
