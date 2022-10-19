namespace ActualChat.Users;

public interface IChatUserSettingsBackend : IComputeService
{
    [ComputeMethod]
    Task<ChatUserSettings?> Get(string userId, string chatId, CancellationToken cancellationToken);
}
