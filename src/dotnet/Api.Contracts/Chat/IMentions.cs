namespace ActualChat.Chat;

/// <summary>
/// Service for tracking mentions of users in chat messages.
/// </summary>
public interface IMentions : IComputeService
{
    [ComputeMethod(MinCacheDuration = 60), RemoteComputeMethod(MinCacheDuration = 600)]
    Task<Mention?> GetLastOwn(
        Session session,
        ChatId chatId,
        CancellationToken cancellationToken);
}
