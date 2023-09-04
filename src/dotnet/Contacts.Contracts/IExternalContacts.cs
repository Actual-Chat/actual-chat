using MemoryPack;

namespace ActualChat.Contacts;

public interface IExternalContacts : IComputeService
{
    [ComputeMethod]
    Task<ApiArray<ExternalContact>> List(Session session, Symbol deviceId, CancellationToken cancellationToken);

    [CommandHandler]
    Task<ExternalContact?> OnChange(ExternalContacts_Change command, CancellationToken cancellationToken);
}

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record ExternalContacts_Change(
    [property: DataMember, MemoryPackOrder(0)] Session Session,
    [property: DataMember, MemoryPackOrder(1)] ExternalContactId Id,
    [property: DataMember, MemoryPackOrder(2)] long? ExpectedVersion,
    [property: DataMember, MemoryPackOrder(3)] Change<ExternalContact> Change
) : ISessionCommand<ExternalContact?>;
