namespace ActualChat.Users;

public interface IUserAuthorsBackend
{
    [ComputeMethod(KeepAliveTime = 10)]
    Task<UserAuthor?> Get(string userId, bool inherit, CancellationToken cancellationToken);

    [CommandHandler]
    public Task SetAvatar(SetAvatarCommand command, CancellationToken cancellationToken);

    [DataContract]
    public sealed record SetAvatarCommand(
        [property: DataMember] string UserId,
        [property: DataMember] string AvatarId
    ) : ICommand<Unit>, IBackendCommand;
}
