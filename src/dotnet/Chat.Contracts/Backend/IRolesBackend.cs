namespace ActualChat.Chat;

public interface IRolesBackend : IComputeService
{
    [ComputeMethod]
    Task<Role?> Get(string chatId, string roleId, CancellationToken cancellationToken);

    [ComputeMethod]
    Task<ImmutableArray<Role>> List(string chatId, string authorId,
        bool isAuthenticated, bool isAnonymous,
        CancellationToken cancellationToken);
    [ComputeMethod]
    Task<ImmutableArray<Role>> ListSystem(string chatId, CancellationToken cancellationToken);
    [ComputeMethod]
    Task<ImmutableArray<Symbol>> ListAuthorIds(string chatId, string roleId, CancellationToken cancellationToken);

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
