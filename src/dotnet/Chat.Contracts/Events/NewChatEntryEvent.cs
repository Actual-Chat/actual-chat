using ActualChat.Events;

namespace ActualChat.Chat.Events;

[DataContract]
public record NewChatEntryEvent(
    [property: DataMember(Order = 0)]
    Symbol ChatId,
    [property: DataMember(Order = 1)]
    long Id,
    [property: DataMember(Order = 2)]
    string AuthorId,
    [property: DataMember(Order = 3)]
    string Content) : IEvent
{
    [IgnoreDataMember]
    public ShardKind ShardKind => ShardKind.Chat;
    [IgnoreDataMember]
    public Symbol ShardKey => ChatId;
}
