using MemoryPack;

namespace ActualChat.Users;

public interface IUserChatSettingsMigrationBackend : IComputeService
{
    [CommandHandler]
    Task<bool> OnMoveChatToPlace(UserChatSettingsMigrationBackend_MoveChatToPlace command, CancellationToken cancellationToken);
}

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record UserChatSettingsMigrationBackend_MoveChatToPlace(
    [property: DataMember, MemoryPackOrder(0)] ChatId ChatId,
    [property: DataMember, MemoryPackOrder(1)] PlaceId PlaceId
) : ICommand<bool>, IBackendCommand;
