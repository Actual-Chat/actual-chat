using MemoryPack;

namespace ActualChat.Users;

public interface IUserMigratorBackend : IComputeService
{
    [CommandHandler]
    Task<bool> OnMoveChatToPlace(UserMigratorBackend_MoveChatToPlace command, CancellationToken cancellationToken);
}

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record UserMigratorBackend_MoveChatToPlace(
    [property: DataMember, MemoryPackOrder(0)] ChatId ChatId,
    [property: DataMember, MemoryPackOrder(1)] PlaceId PlaceId
) : ICommand<bool>, IBackendCommand;
