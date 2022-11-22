namespace ActualChat.Chat.Db;

[DataContract]
public sealed record DbChatEntryShardRef(
    [property: DataMember(Order = 0)] ChatId ChatId,
    [property: DataMember(Order = 1)] ChatEntryKind Kind)
{
    public override string ToString() => $"{ChatId}:{Kind}";
}
