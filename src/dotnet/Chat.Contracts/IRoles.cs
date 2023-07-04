using MemoryPack;

namespace ActualChat.Chat;

public interface IRoles : IComputeService
{
    [ComputeMethod]
    Task<Role?> Get(Session session, ChatId chatId, RoleId roleId, CancellationToken cancellationToken);

    [ComputeMethod]
    Task<ApiArray<Role>> List(Session session, ChatId chatId, CancellationToken cancellationToken);
    [ComputeMethod]
    Task<ApiArray<AuthorId>> ListAuthorIds(Session session, ChatId chatId, RoleId roleId, CancellationToken cancellationToken);
    [ComputeMethod]
    Task<ApiArray<AuthorId>> ListOwnerIds(Session session, ChatId chatId, CancellationToken cancellationToken);

    [CommandHandler]
    Task<Role> OnChange(Roles_Change command, CancellationToken cancellationToken);
}

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record Roles_Change(
    [property: DataMember, MemoryPackOrder(0)] Session Session,
    [property: DataMember, MemoryPackOrder(1)] ChatId ChatId,
    [property: DataMember, MemoryPackOrder(2)] RoleId RoleId,
    [property: DataMember, MemoryPackOrder(3)] long? ExpectedVersion,
    [property: DataMember, MemoryPackOrder(4)] Change<RoleDiff> Change
) : ISessionCommand<Role>;
