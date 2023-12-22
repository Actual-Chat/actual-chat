using MemoryPack;

namespace ActualChat.Contacts;

public interface IContactsMigrationBackend : IComputeService
{
    [CommandHandler]
    Task OnMoveChatToPlace(ContactsMigrationBackend_MoveChatToPlace command, CancellationToken cancellationToken);
}

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record ContactsMigrationBackend_MoveChatToPlace(
    [property: DataMember, MemoryPackOrder(0)] ChatId ChatId,
    [property: DataMember, MemoryPackOrder(1)] PlaceId PlaceId
) : ICommand<Unit>, IBackendCommand;
