namespace ActualChat.Chat.Db;

public sealed record DbChatEntryShardRef(string ChatId, ChatEntryKind Kind)
{
    public override string ToString()
        => $"{ChatId}:{Kind}";
}
