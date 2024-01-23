using MemoryPack;

namespace ActualChat.Contacts;

public interface IContactsBackend : IComputeService
{
    [ComputeMethod]
    Task<Contact> Get(UserId ownerId, ContactId contactId, CancellationToken cancellationToken);
    [ComputeMethod]
    Task<ApiArray<ContactId>> ListIdsForSearch(UserId userId, CancellationToken cancellationToken);
    [ComputeMethod]
    Task<ApiArray<ContactId>> ListIds(UserId ownerId, PlaceId placeId, CancellationToken cancellationToken);
    [ComputeMethod]
    Task<ApiArray<PlaceId>> ListPlaceIds(UserId ownerId, CancellationToken cancellationToken);
    [CommandHandler]
    Task<Contact?> OnChange(ContactsBackend_Change command, CancellationToken cancellationToken);
    [CommandHandler]
    Task OnTouch(ContactsBackend_Touch command, CancellationToken cancellationToken);
    [CommandHandler]
    Task OnRemoveAccount(ContactsBackend_RemoveAccount command, CancellationToken cancellationToken);
    [CommandHandler]
    Task OnRemoveChatContacts(ContactsBackend_RemoveChatContacts command, CancellationToken cancellationToken);
    [CommandHandler]
    Task OnGreet(ContactsBackend_Greet command, CancellationToken cancellationToken);
    [CommandHandler]
    Task OnChangePlaceMembership(ContactsBackend_ChangePlaceMembership command, CancellationToken cancellationToken);
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

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record ContactsBackend_RemoveAccount(
    [property: DataMember, MemoryPackOrder(0)] UserId UserId
) : ICommand<Unit>, IBackendCommand;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record ContactsBackend_RemoveChatContacts(
    [property: DataMember, MemoryPackOrder(0)] ChatId ChatId
) : ICommand<Unit>, IBackendCommand;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record ContactsBackend_Greet(
    [property: DataMember, MemoryPackOrder(0)] UserId UserId
) : ICommand<Unit>, IBackendCommand;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record ContactsBackend_ChangePlaceMembership(
    [property: DataMember, MemoryPackOrder(0)] UserId OwnerId,
    [property: DataMember, MemoryPackOrder(1)] PlaceId PlaceId,
    [property: DataMember, MemoryPackOrder(2)] bool HasLeft
) : ICommand<Unit>, IBackendCommand;
