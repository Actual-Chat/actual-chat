using MemoryPack;

namespace ActualChat.Contacts;

public interface IExternalContactHashes : IComputeService
{
    [ComputeMethod]
    Task<ExternalContactsHash?> Get(Session session, Symbol deviceId, CancellationToken cancellationToken);
    [CommandHandler]
    Task<ExternalContactsHash?> OnChange(ExternalContactHashes_Change command, CancellationToken cancellationToken);
}

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record ExternalContactHashes_Change(
    [property: DataMember, MemoryPackOrder(0)] Session Session,
    [property: DataMember, MemoryPackOrder(1)] Symbol DeviceId,
    [property: DataMember, MemoryPackOrder(2)] long? ExpectedVersion,
    [property: DataMember, MemoryPackOrder(3)] Change<ExternalContactsHash> Change
) : ISessionCommand<ExternalContactsHash?>, IApiCommand;
