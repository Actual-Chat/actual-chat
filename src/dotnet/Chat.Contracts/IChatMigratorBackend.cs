using MemoryPack;

namespace ActualChat.Chat;

public interface IChatMigratorBackend : IComputeService
{
    [CommandHandler]
    Task OnMoveToPlace(ChatMigratorBackend_MoveChatToPlace command, CancellationToken cancellationToken);
}

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record ChatMigratorBackend_MoveChatToPlace(
    [property: DataMember, MemoryPackOrder(0)] ChatId ChatId,
    [property: DataMember, MemoryPackOrder(1)] PlaceId PlaceId
) : ICommand<Unit>, IBackendCommand;
