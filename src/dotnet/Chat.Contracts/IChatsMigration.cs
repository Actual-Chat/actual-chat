using MemoryPack;

namespace ActualChat.Chat;

public interface IChatsMigration : IComputeService
{
    [CommandHandler]
    Task<bool> OnMoveToPlace(ChatsMigration_MoveToPlace command, CancellationToken cancellationToken);
}

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record ChatsMigration_MoveToPlace(
    [property: DataMember, MemoryPackOrder(0)] Session Session,
    [property: DataMember, MemoryPackOrder(1)] ChatId ChatId,
    [property: DataMember, MemoryPackOrder(2)] PlaceId PlaceId
) : ISessionCommand<bool>;
