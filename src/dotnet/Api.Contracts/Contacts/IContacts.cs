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
    [ComputeMethod(MinCacheDuration = 300), RemoteComputeMethod(MinCacheDuration = 600)]
    Task<ContactId[]> ListBlockedIds(Session session, CancellationToken cancellationToken);

    [CommandHandler]
    Task<Contact?> OnChange(Contacts_Change command, CancellationToken cancellationToken);
    [CommandHandler]
    Task OnTouch(Contacts_Touch command, CancellationToken cancellationToken);
    [CommandHandler]
    Task OnSetIsBlocked(Contacts_SetIsBlocked command, CancellationToken cancellationToken);
}

[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record Contacts_Touch(
    [property: DataMember, Key(0)] Session Session,
    [property: DataMember, Key(1)] ContactId Id
) : ISessionCommand<Unit>, IApiCommand;

[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record Contacts_SetIsBlocked(
    [property: DataMember, Key(0)] Session Session,
    [property: DataMember, Key(1)] ContactId Id,
    [property: DataMember, Key(2)] bool IsBlocked
) : ISessionCommand<Unit>, IApiCommand;

[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record Contacts_Change(
    [property: DataMember, Key(0)] Session Session,
    [property: DataMember, Key(1)] ContactId Id,
    [property: DataMember, Key(2)] long? ExpectedVersion,
    [property: DataMember, Key(3)] Change<Contact> Change
) : ISessionCommand<Contact?>, IApiCommand;
