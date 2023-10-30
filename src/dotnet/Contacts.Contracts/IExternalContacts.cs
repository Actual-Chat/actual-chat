﻿using MemoryPack;

namespace ActualChat.Contacts;

public interface IExternalContacts : IComputeService
{
    [ComputeMethod]
    Task<ApiArray<ExternalContact>> List(Session session, Symbol deviceId, CancellationToken cancellationToken);

    [Obsolete("2023.10: Replaced with OnBulkChange")]
    [CommandHandler]
    Task<ExternalContact?> OnChange(ExternalContacts_Change command, CancellationToken cancellationToken);

    [CommandHandler]
    Task<ApiArray<ChangeResult<ExternalContact>>> OnBulkChange(ExternalContacts_BulkChange command, CancellationToken cancellationToken);
}

[Obsolete("2023.10: Replaced with ExternalContacts_BulkChange")]
[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record ExternalContacts_Change(
    [property: DataMember, MemoryPackOrder(0)] Session Session,
    [property: DataMember, MemoryPackOrder(1)] ExternalContactId Id,
    [property: DataMember, MemoryPackOrder(2)] long? ExpectedVersion,
    [property: DataMember, MemoryPackOrder(3)] Change<ExternalContact> Change
) : ISessionCommand<ExternalContact?>;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record ExternalContacts_BulkChange(
    [property: DataMember, MemoryPackOrder(0)] Session Session,
    [property: DataMember, MemoryPackOrder(1)] ApiArray<ExternalContactChange> Changes
) : ISessionCommand<ApiArray<ChangeResult<ExternalContact>>>;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial record ExternalContactChange(
    [property: DataMember, MemoryPackOrder(1)] ExternalContactId Id,
    [property: DataMember, MemoryPackOrder(2)] long? ExpectedVersion,
    [property: DataMember, MemoryPackOrder(3)] Change<ExternalContact> Change
);
