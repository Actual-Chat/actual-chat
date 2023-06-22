using MemoryPack;

namespace ActualChat.Contacts;

public interface IContactsBackend : IComputeService
{
    [ComputeMethod]
    public Task<Contact> Get(UserId ownerId, ContactId contactId, CancellationToken cancellationToken);
    [ComputeMethod]
    public Task<ApiArray<ContactId>> ListIds(UserId ownerId, CancellationToken cancellationToken);

    [CommandHandler]
    public Task<Contact?> OnChange(ContactsBackend_Change command, CancellationToken cancellationToken);
    [CommandHandler]
    public Task OnTouch(ContactsBackend_Touch command, CancellationToken cancellationToken);
}

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record ContactsBackend_Change(
    [property: DataMember, MemoryPackOrder(0)] ContactId Id,
    [property: DataMember, MemoryPackOrder(1)] long? ExpectedVersion,
    [property: DataMember, MemoryPackOrder(2)] Change<Contact> Change
) : ICommand<Contact?>, IBackendCommand;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record ContactsBackend_Touch(
    [property: DataMember, MemoryPackOrder(0)] ContactId Id
) : ICommand<Unit>, IBackendCommand;
