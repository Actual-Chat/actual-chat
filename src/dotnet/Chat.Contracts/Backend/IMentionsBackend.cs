namespace ActualChat.Chat;

public interface IMentionsBackend : IComputeService
{
    [ComputeMethod]
    Task<Mention?> GetLast(
        ChatId chatId,
        Symbol mentionId,
        CancellationToken cancellationToken);
}
