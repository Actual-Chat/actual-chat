namespace ActualChat.Users;

public interface IUserAvatarsBackend
{
    [ComputeMethod(KeepAliveTime = 10)]
    Task<UserAvatar?> Get(string avatarId, CancellationToken cancellationToken);
    Task<string> GetAvatarIdByChatAuthorId(string chatAuthorId, CancellationToken cancellationToken);
    [ComputeMethod(KeepAliveTime = 10)]
    Task<string[]> GetAvatarIds(string userId, CancellationToken cancellationToken);
    Task<UserAvatar> EnsureChatAuthorAvatarCreated(string chatAuthorId, string name, CancellationToken cancellationToken);

    [CommandHandler]
    Task<UserAvatar> Create(CreateCommand command, CancellationToken cancellationToken);
    [CommandHandler]
    Task Update(UpdateCommand command, CancellationToken cancellationToken);

    [DataContract]
    public record CreateCommand(
        [property: DataMember] string PrincipalId,
        [property: DataMember] string Name
        ) : ICommand<UserAvatar>, IBackendCommand;

    [DataContract]
    public record UpdateCommand(
        [property: DataMember] string AvatarId,
        [property: DataMember] string Name,
        [property: DataMember] string Picture,
        [property: DataMember] string Bio
        ) : ICommand<Unit>, IBackendCommand;
}
