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
public sealed partial record Contacts_Touch : ApiCommand<Unit>
{
    [DataMember(Order = 2), Key(2)] public required ContactId Id { get; init; }
}

[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record Contacts_SetIsBlocked : ApiCommand<Unit>
{
    [DataMember(Order = 2), Key(2)] public required ContactId Id { get; init; }
    [DataMember(Order = 3), Key(3)] public required bool IsBlocked { get; init; }
}

[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record Contacts_Change : ApiCommand<Contact?>
{
    [DataMember(Order = 2), Key(2)] public required ContactId Id { get; init; }
    [DataMember(Order = 3), Key(3)] public required long? ExpectedVersion { get; init; }
    [DataMember(Order = 4), Key(4)] public required Change<Contact> Change { get; init; }
}
