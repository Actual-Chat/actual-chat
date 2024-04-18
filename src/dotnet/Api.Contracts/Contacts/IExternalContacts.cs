using MemoryPack;

namespace ActualChat.Contacts;

public interface IExternalContacts : IComputeService
{
    [ComputeMethod, Obsolete("2024.04: Not available for clients anymore")]
    // TODO(FC): Change to ListV1 when API backward compatibility attributes are supported
    Task<ApiArray<ExternalContactFull>> List(Session session, Symbol deviceId, CancellationToken cancellationToken);
    [ComputeMethod]
    // TODO: Change to List when API backward compatibility attributes are supported
    Task<ApiArray<ExternalContact>> List2(Session session, Symbol deviceId, CancellationToken cancellationToken);
    [CommandHandler, Obsolete("2023.10: Replaced with OnBulkChange.")]
    Task<ExternalContactFull?> OnChange(ExternalContacts_Change command, CancellationToken cancellationToken);
    [CommandHandler]
    Task<ApiArray<Result<ExternalContactFull?>>> OnBulkChange(ExternalContacts_BulkChange command, CancellationToken cancellationToken);
}

[Obsolete("2023.10: Replaced with ExternalContacts_BulkChange.")]
[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record ExternalContacts_Change(
    [property: DataMember, MemoryPackOrder(0)] Session Session,
    [property: DataMember, MemoryPackOrder(1)] ExternalContactId Id,
    [property: DataMember, MemoryPackOrder(2)] long? ExpectedVersion,
    [property: DataMember, MemoryPackOrder(3)] Change<ExternalContactFull> Change
) : ISessionCommand<ExternalContactFull?>, IApiCommand;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record ExternalContacts_BulkChange(
    [property: DataMember, MemoryPackOrder(0)] Session Session,
    [property: DataMember, MemoryPackOrder(1)] ApiArray<ExternalContactChange> Changes
) : ISessionCommand<ApiArray<Result<ExternalContactFull?>>>, IApiCommand;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial record ExternalContactChange(
    [property: DataMember, MemoryPackOrder(1)] ExternalContactId Id,
    [property: DataMember, MemoryPackOrder(2)] long? ExpectedVersion,
    [property: DataMember, MemoryPackOrder(3)] Change<ExternalContactFull> Change
);
