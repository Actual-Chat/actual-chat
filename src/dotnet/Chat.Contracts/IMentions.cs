namespace ActualChat.Chat;

public interface IMentions : IComputeService
{
    [ComputeMethod(MinCacheDuration = 60)]
    Task<Mention?> GetLastOwn(
        Session session,
        Symbol chatId,
        CancellationToken cancellationToken);
}
