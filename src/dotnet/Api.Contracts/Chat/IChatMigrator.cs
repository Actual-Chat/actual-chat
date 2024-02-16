using MemoryPack;

namespace ActualChat.Chat;

public interface IChatMigrator : IComputeService
{
    [CommandHandler]
    Task<bool> OnMoveToPlace(ChatMigrator_MoveChatToPlace command, CancellationToken cancellationToken);
}

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record ChatMigrator_MoveChatToPlace(
    [property: DataMember, MemoryPackOrder(0)] Session Session,
    [property: DataMember, MemoryPackOrder(1)] ChatId ChatId,
    [property: DataMember, MemoryPackOrder(2)] PlaceId PlaceId
) : ISessionCommand<bool>;
