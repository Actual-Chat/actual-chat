namespace ActualChat.Contacts;

/// <summary>
/// Service for managing user contacts and contact lists.
/// </summary>
public interface IContacts : IComputeService
{
    [ComputeMethod, RemoteComputeMethod(MinCacheDuration = 600)]
    Task<Contact?> Get(Session session, ContactId contactId, CancellationToken cancellationToken);
    [ComputeMethod, RemoteComputeMethod(MinCacheDuration = 600)]
    Task<Contact?> GetForChat(Session session, ChatId chatId, CancellationToken cancellationToken);

    [ComputeMethod(MinCacheDuration = 300), RemoteComputeMethod(MinCacheDuration = 600)]
    Task<ContactId[]> ListIds(Session session, PlaceId? placeId, CancellationToken cancellationToken);
    [ComputeMethod(MinCacheDuration = 300), RemoteComputeMethod(MinCacheDuration = 600)]
    Task<PlaceId[]> ListPlaceIds(Session session, CancellationToken cancellationToken);

    [CommandHandler]
    Task<Contact?> OnChange(Contacts_Change command, CancellationToken cancellationToken);
    [CommandHandler]
    Task OnTouch(Contacts_Touch command, CancellationToken cancellationToken);
}

[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record Contacts_Touch(
    [property: DataMember, MemoryPackOrder(0), Key(0)] Session Session,
    [property: DataMember, MemoryPackOrder(1), Key(1)] ContactId Id
) : ISessionCommand<Unit>, IApiCommand;

[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record Contacts_Change(
    [property: DataMember, MemoryPackOrder(0), Key(0)] Session Session,
    [property: DataMember, MemoryPackOrder(1), Key(1)] ContactId Id,
    [property: DataMember, MemoryPackOrder(2), Key(2)] long? ExpectedVersion,
    [property: DataMember, MemoryPackOrder(3), Key(3)] Change<Contact> Change
) : ISessionCommand<Contact?>, IApiCommand;
