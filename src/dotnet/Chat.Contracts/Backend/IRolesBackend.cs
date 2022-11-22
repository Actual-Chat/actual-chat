namespace ActualChat.Chat;

public interface IRolesBackend : IComputeService
{
    [ComputeMethod]
    Task<Role?> Get(ChatId chatId, RoleId roleId, CancellationToken cancellationToken);

    [ComputeMethod]
    Task<ImmutableArray<Role>> List(ChatId chatId, AuthorId authorId,
        bool isAuthenticated, bool isAnonymous,
        CancellationToken cancellationToken);
    [ComputeMethod]
    Task<ImmutableArray<Role>> ListSystem(ChatId chatId, CancellationToken cancellationToken);
    [ComputeMethod]
    Task<ImmutableArray<AuthorId>> ListAuthorIds(ChatId chatId, RoleId roleId, CancellationToken cancellationToken);

    [CommandHandler]
    Task<Role> Change(ChangeCommand command, CancellationToken cancellationToken);

    [DataContract]
    public sealed record ChangeCommand(
        [property: DataMember] ChatId ChatId,
        [property: DataMember] RoleId RoleId,
        [property: DataMember] long? ExpectedVersion,
        [property: DataMember] Change<RoleDiff> Change
    ) : ICommand<Role>, IBackendCommand;
}
