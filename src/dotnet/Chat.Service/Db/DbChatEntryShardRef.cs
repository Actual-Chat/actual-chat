namespace ActualChat.Chat.Db;

public sealed record DbChatEntryShardRef(string ChatId, ChatEntryType Type)
{
    public override string ToString()
        => $"{ChatId}:{Type}";
}
