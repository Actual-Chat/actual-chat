using MemoryPack;

namespace ActualChat.Chat;

public interface IChatsMigrationBackend : IComputeService
{
    [CommandHandler]
    Task OnMoveToPlace(ChatsMigrationBackend_MoveToPlace command, CancellationToken cancellationToken);
}

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record ChatsMigrationBackend_MoveToPlace(
    [property: DataMember, MemoryPackOrder(0)] ChatId ChatId,
    [property: DataMember, MemoryPackOrder(1)] PlaceId PlaceId
) : ICommand<Unit>, IBackendCommand;
