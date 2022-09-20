namespace ActualChat.Chat;

public interface IMentionsBackend
{
    [ComputeMethod]
    Task<Mention?> GetLast(
        Symbol chatId,
        Symbol authorId,
        CancellationToken cancellationToken);
}
