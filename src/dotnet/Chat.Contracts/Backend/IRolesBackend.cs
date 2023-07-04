using MemoryPack;

namespace ActualChat.Chat;

public interface IRolesBackend : IComputeService
{
    [ComputeMethod]
    Task<Role?> Get(ChatId chatId, RoleId roleId, CancellationToken cancellationToken);

    [ComputeMethod]
    Task<ApiArray<Role>> List(
        ChatId chatId, AuthorId authorId,
        bool isGuest, bool isAnonymous,
        CancellationToken cancellationToken);
    [ComputeMethod]
    Task<ApiArray<Role>> ListSystem(ChatId chatId, CancellationToken cancellationToken);
    [ComputeMethod]
    Task<ApiArray<AuthorId>> ListAuthorIds(ChatId chatId, RoleId roleId, CancellationToken cancellationToken);

    [CommandHandler]
    Task<Role> Change(RolesBackend_Change command, CancellationToken cancellationToken);
}

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record RolesBackend_Change(
    [property: DataMember, MemoryPackOrder(0)] ChatId ChatId,
    [property: DataMember, MemoryPackOrder(1)] RoleId RoleId,
    [property: DataMember, MemoryPackOrder(2)] long? ExpectedVersion,
    [property: DataMember, MemoryPackOrder(3)] Change<RoleDiff> Change
) : ICommand<Role>, IBackendCommand;
