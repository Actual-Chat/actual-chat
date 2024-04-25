using ActualLab.Rpc;
using MemoryPack;

namespace ActualChat.Contacts;

public interface IExternalContacts : IComputeService
{
    [ComputeMethod]
    Task<ApiArray<ExternalContact>> List(Session session, Symbol deviceId, CancellationToken cancellationToken);
    [CommandHandler, Obsolete("2023.10: Replaced with OnBulkChange.")]
    Task<ExternalContactFull?> OnChange(ExternalContacts_Change command, CancellationToken cancellationToken);
    [CommandHandler]
    Task<ApiArray<Result<ExternalContactFull?>>> OnBulkChange(ExternalContacts_BulkChange command, CancellationToken cancellationToken);

    // Legacy methods
    [ComputeMethod, LegacyName("List", "v1.10.999.0"), Obsolete("2024.04: Replaced with new List implementation.")]
    Task<ApiArray<ExternalContactFull>> LegacyList1(Session session, Symbol deviceId, CancellationToken cancellationToken);
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
