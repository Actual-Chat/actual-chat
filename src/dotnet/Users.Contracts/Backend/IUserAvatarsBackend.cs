namespace ActualChat.Users;

public interface IUserAvatarsBackend : IComputeService
{
    [ComputeMethod]
    Task<UserAvatar?> Get(string avatarId, CancellationToken cancellationToken);
    [ComputeMethod]
    Task<ImmutableArray<Symbol>> ListAvatarIds(string userId, CancellationToken cancellationToken);

    Task<Symbol> GetAvatarIdByChatAuthorId(string chatAuthorId, CancellationToken cancellationToken);
    Task<UserAvatar> EnsureChatAuthorAvatarCreated(string chatAuthorId, string name, CancellationToken cancellationToken);

    [CommandHandler]
    Task<UserAvatar> Create(CreateCommand command, CancellationToken cancellationToken);
    [CommandHandler]
    Task Update(UpdateCommand command, CancellationToken cancellationToken);

    [DataContract]
    public sealed record CreateCommand(
        [property: DataMember] string PrincipalId,
        [property: DataMember] string Name
        ) : ICommand<UserAvatar>, IBackendCommand;

    [DataContract]
    public sealed record UpdateCommand(
        [property: DataMember] string AvatarId,
        [property: DataMember] string Name,
        [property: DataMember] string Picture,
        [property: DataMember] string Bio
        ) : ICommand<Unit>, IBackendCommand;
}
