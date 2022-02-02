namespace ActualChat.Users;

public interface IUserAvatarsBackend
{
    [ComputeMethod(KeepAliveTime = 10)]
    Task<UserAvatar?> Get(string avatarId, CancellationToken cancellationToken);
    Task<string> GetAvatarIdByChatAuthorId(string chatAuthorId, CancellationToken cancellationToken);
    [ComputeMethod(KeepAliveTime = 10)]
    Task<string[]> GetAvatarIds(string userId, CancellationToken cancellationToken);
    Task EnsureChatAuthorAvatar(string chatAuthorId, string name, CancellationToken cancellationToken);

    [CommandHandler]
    Task<UserAvatar> Create(CreateCommand command, CancellationToken cancellationToken);

    [CommandHandler]
    Task Update(UpdateCommand command, CancellationToken cancellationToken);

    public record CreateCommand(string PrincipalId, string Name) : ICommand<UserAvatar>, IBackendCommand;
    public record UpdateCommand(string AvatarId, string Name, string Picture, string Bio) : ICommand<Unit>, IBackendCommand;
}
