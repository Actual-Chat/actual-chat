using MemoryPack;

namespace ActualChat.Contacts;

public interface IContactMigratorBackend : IComputeService
{
    [CommandHandler]
    Task OnMoveChatToPlace(ContactMigratorBackend_MoveChatToPlace command, CancellationToken cancellationToken);
}

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record ContactMigratorBackend_MoveChatToPlace(
    [property: DataMember, MemoryPackOrder(0)] ChatId ChatId,
    [property: DataMember, MemoryPackOrder(1)] PlaceId PlaceId
) : ICommand<Unit>, IBackendCommand;
