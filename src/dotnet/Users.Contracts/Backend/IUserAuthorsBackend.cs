namespace ActualChat.Users;

public interface IUserAuthorsBackend
{
    [ComputeMethod(KeepAliveTime = 10)]
    Task<UserAuthor?> Get(string userId, bool inherit, CancellationToken cancellationToken);

    [CommandHandler]
    public Task SetAvatar(SetAvatarCommand command, CancellationToken cancellationToken);

    public record SetAvatarCommand(string UserId, string AvatarId) : ICommand<Unit>, IBackendCommand;
}
