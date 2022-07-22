namespace ActualChat.Chat;

public interface IChatRolesBackend : IComputeService
{
    [ComputeMethod]
    Task<ChatRole?> Get(string chatId, string roleId, CancellationToken cancellationToken);

    [ComputeMethod]
    Task<ImmutableArray<ChatRole>> List(string chatId, string? authorId, bool isAuthenticated, bool isAdmin, CancellationToken cancellationToken);
    [ComputeMethod]
    Task<ImmutableArray<ChatRole>> ListSystem(string chatId, CancellationToken cancellationToken);
    [ComputeMethod]
    Task<ImmutableArray<Symbol>> ListAuthorIds(string chatId, string roleId, CancellationToken cancellationToken);

    [CommandHandler]
    Task<ChatRole?> Change(ChangeCommand command, CancellationToken cancellationToken);

    [DataContract]
    public sealed record ChangeCommand(
        [property: DataMember] string ChatId,
        [property: DataMember] string RoleId,
        [property: DataMember] long? ExpectedVersion,
        [property: DataMember] Change<ChatRoleDiff> Change
    ) : ICommand<ChatRole?>;
}
