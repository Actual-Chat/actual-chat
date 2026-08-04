using ActualLab.Rpc;

namespace ActualChat.Contacts;

/// <summary>
/// Backend service for managing user contacts and memberships.
/// </summary>
public interface IContactsBackend : IComputeService, IBackendService
{
    [ComputeMethod]
    Task<Contact> Get(UserId ownerId, ContactId contactId, CancellationToken cancellationToken);
    [ComputeMethod]
    Task<ContactId[]> ListIdsForSearch(UserId userId, ContactSubset contactSubset, bool includePublic, CancellationToken cancellationToken);
    [ComputeMethod]
    Task<ContactId[]> ListIdsForGroupContactSearch(UserId userId, ContactSubset contactSubset, CancellationToken cancellationToken);
    [ComputeMethod]
    Task<ContactId[]> ListPeerContactIds(UserId userId, PlaceId? placeId, CancellationToken cancellationToken);
    [ComputeMethod]
    Task<ContactId[]> ListIds(UserId ownerId, PlaceId? placeId, CancellationToken cancellationToken);
    [ComputeMethod]
    Task<ContactId[]> ListBlockedIds(UserId ownerId, CancellationToken cancellationToken);
    [ComputeMethod]
    Task<bool> IsBlocked(UserId ownerId, UserId otherUserId, CancellationToken cancellationToken);
    [ComputeMethod]
    Task<PlaceId[]> ListPlaceIds(UserId ownerId, CancellationToken cancellationToken);
    [ComputeMethod]
    Task<ThreadContact?> GetThreadContact(UserId ownerId, ContactId contactId, CancellationToken cancellationToken);
    [ComputeMethod]
    Task<ThreadChatId[]> ListThreadIdsForChat(UserId ownerId, ChatId parentChatId, CancellationToken cancellationToken);
    [ComputeMethod]
    Task<ThreadChatId[]> ListThreadIdsForPlace(UserId ownerId, PlaceId? parentPlaceId, CancellationToken cancellationToken);

    // Non-compute methods
    Task<Contact[]> ListChangedPeerContacts(ChangedContactsQuery query, CancellationToken cancellationToken);
    Task<Contact[]> ListChangedPlaceContacts(ChangedContactsQuery query, CancellationToken cancellationToken);

    // Commands

    [CommandHandler]
    Task<Contact?> OnChange(ContactsBackend_Change command, CancellationToken cancellationToken);
    [CommandHandler]
    Task OnTouch(ContactsBackend_Touch command, CancellationToken cancellationToken);
    [CommandHandler]
    Task OnSetIsBlocked(ContactsBackend_SetIsBlocked command, CancellationToken cancellationToken);
    [CommandHandler]
    Task OnRemoveAccount(ContactsBackend_RemoveAccount command, CancellationToken cancellationToken);
    [CommandHandler]
    Task OnRemoveChatContacts(ContactsBackend_RemoveChatContacts command, CancellationToken cancellationToken);
    [CommandHandler]
    Task OnGreet(ContactsBackend_Greet command, CancellationToken cancellationToken);
    [CommandHandler]
    Task OnChangePlaceMembership(ContactsBackend_ChangePlaceMembership command, CancellationToken cancellationToken);
    [CommandHandler]
    Task OnPublishCopiedChat(ContactsBackend_PublishCopiedChat command, CancellationToken cancellationToken);
    [CommandHandler]
    Task OnReviewExternalContactName(ContactsBackend_ReviewExternalContactName command, CancellationToken cancellationToken);
    [CommandHandler]
    Task<ThreadContact?> OnChangeThreadContact(ContactsBackend_ChangeThreadContact command, CancellationToken cancellationToken);

    // Events

    [EventHandler]
    Task OnChatChangedEvent(ChatChangedEvent eventCommand, CancellationToken cancellationToken);
    [EventHandler]
    Task OnAuthorChangedEvent(AuthorUpsertedEvent eventCommand, CancellationToken cancellationToken);
    [EventHandler]
    Task OnChatEntryChangedEvent(ChatEntryChangedEvent eventCommand, CancellationToken cancellationToken);
    [EventHandler]
    Task OnExternalContactNameMayHaveChangedEvent(
        ExternalContactNameMayHaveChangedEvent eventCommand,
        CancellationToken cancellationToken);
    [EventHandler]
    Task OnUserMentionedInThreadChatEvent(
        UserMentionedInThreadChatEvent eventCommand,
        CancellationToken cancellationToken);
}

/// <summary>
/// Command to create, update, or delete a contact.
/// </summary>
[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record ContactsBackend_Change(
    [property: DataMember, Key(0)] ContactId Id,
    [property: DataMember, Key(1)] long? ExpectedVersion,
    [property: DataMember, Key(2)] Change<Contact> Change
) : ICommand<Contact?>, IBackendCommand, IHasShardKey<ContactId>
{
    [IgnoreDataMember, IgnoreMember]
    public ContactId ShardKey => Id;
}

/// <summary>
/// Command to update a contact's last activity timestamp.
/// </summary>
[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record ContactsBackend_Touch(
    [property: DataMember, Key(0)] ContactId Id
) : ICommand<Unit>, IBackendCommand, IHasShardKey<ContactId>
{
    [IgnoreDataMember, IgnoreMember]
    public ContactId ShardKey => Id;
}

/// <summary>
/// Command to set a peer contact's blocked flag.
/// </summary>
[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record ContactsBackend_SetIsBlocked(
    [property: DataMember, Key(0)] ContactId Id,
    [property: DataMember, Key(1)] bool IsBlocked
) : ICommand<Unit>, IBackendCommand, IHasShardKey<ContactId>
{
    [IgnoreDataMember, IgnoreMember]
    public ContactId ShardKey => Id;
}

/// <summary>
/// Command to remove all contacts for a deleted account.
/// </summary>
[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record ContactsBackend_RemoveAccount(
    [property: DataMember, Key(0)] UserId UserId
) : ICommand<Unit>, IBackendCommand, IHasShardKey<UserId>
{
    [IgnoreDataMember, IgnoreMember]
    public UserId ShardKey => UserId;
}

/// <summary>
/// Command to remove all contacts for a deleted chat.
/// </summary>
[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record ContactsBackend_RemoveChatContacts(
    [property: DataMember, Key(0)] ChatId ChatId
) : ICommand<Unit>, IBackendCommand, IHasShardKey<ChatId>
{
    [IgnoreDataMember, IgnoreMember]
    public ChatId ShardKey => ChatId;
}

/// <summary>
/// Command to send welcome messages to a new user.
/// </summary>
[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record ContactsBackend_Greet(
    [property: DataMember, Key(0)] UserId UserId
) : ICommand<Unit>, IBackendCommand, IHasShardKey<UserId>
{
    [IgnoreDataMember, IgnoreMember]
    public UserId ShardKey => UserId;
}

/// <summary>
/// Command to update a user's membership status in a place.
/// </summary>
[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record ContactsBackend_ChangePlaceMembership(
    [property: DataMember, Key(0)] PlaceId PlaceId,
    [property: DataMember, Key(1)] UserId OwnerId,
    [property: DataMember, Key(2)] bool HasLeft
) : ICommand<Unit>, IBackendCommand, IHasShardKey<PlaceId>
{
    [IgnoreDataMember, IgnoreMember]
    public PlaceId ShardKey => PlaceId;
}

/// <summary>
/// Command to publish a chat that was copied to a place.
/// </summary>
[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record ContactsBackend_PublishCopiedChat(
    [property: DataMember, Key(0)] PlaceChatId ChatId
) : ICommand<Unit>, IBackendCommand, IHasShardKey<ChatId>
{
    [IgnoreDataMember, IgnoreMember]
    public ChatId ShardKey => ChatId;
}

/// <summary>
/// Command to review and update a contact's name from external contacts.
/// </summary>
[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record ContactsBackend_ReviewExternalContactName(
    [property: DataMember, Key(0)] ContactId Id
) : ICommand<Unit>, IBackendCommand, IHasShardKey<ContactId>
{
    [IgnoreDataMember, IgnoreMember]
    public ContactId ShardKey => Id;
}

/// <summary>
/// Command to create, update, or delete a thread contact.
/// </summary>
[DataContract, MessagePackObject]
// ReSharper disable once InconsistentNaming
public sealed partial record ContactsBackend_ChangeThreadContact(
    [property: DataMember, Key(0)] ContactId Id,
    [property: DataMember, Key(1)] long? ExpectedVersion,
    [property: DataMember, Key(2)] Change<ThreadContact> Change
) : ICommand<ThreadContact?>, IBackendCommand, IHasShardKey<ContactId>
{
    [IgnoreDataMember, IgnoreMember]
    public ContactId ShardKey => Id;
}
