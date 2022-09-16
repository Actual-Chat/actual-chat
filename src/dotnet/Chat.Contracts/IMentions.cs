namespace ActualChat.Chat;

public interface IMentions : IComputeService
{
    [ComputeMethod(MinCacheDuration = 60)]
    Task<Mention?> GetLast(
        Session session,
        Symbol chatId,
        CancellationToken cancellationToken);
}
