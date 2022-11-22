namespace ActualChat.Chat;

public interface IMentionsBackend
{
    [ComputeMethod]
    Task<Mention?> GetLast(
        ChatId chatId,
        Symbol mentionId,
        CancellationToken cancellationToken);
}
