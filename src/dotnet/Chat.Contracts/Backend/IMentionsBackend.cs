namespace ActualChat.Chat;

public interface IMentionsBackend
{
    [ComputeMethod]
    Task<Mention?> GetLast(
        string chatId,
        string authorId,
        CancellationToken cancellationToken);
}
