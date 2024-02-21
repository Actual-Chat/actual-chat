using MemoryPack;

namespace ActualChat.Contacts;

public interface IContacts : IComputeService
{
    [ComputeMethod]
    Task<Contact?> Get(Session session, ContactId contactId, CancellationToken cancellationToken);
    [ComputeMethod]
    Task<Contact?> GetForChat(Session session, ChatId chatId, CancellationToken cancellationToken);
    [ComputeMethod(MinCacheDuration = 300)]
    Task<ApiArray<ContactId>> ListIds(Session session, CancellationToken cancellationToken);
    [ComputeMethod(MinCacheDuration = 300)]
    Task<ApiArray<PlaceId>> ListPlaceIds(Session session, CancellationToken cancellationToken);
    [ComputeMethod(MinCacheDuration = 300)]
    Task<ApiArray<ContactId>> ListIds(Session session, PlaceId placeId, CancellationToken cancellationToken);

    [CommandHandler]
    Task<Contact?> OnChange(Contacts_Change command, CancellationToken cancellationToken);
    [CommandHandler]
    Task OnTouch(Contacts_Touch command, CancellationToken cancellationToken);
    [CommandHandler, Obsolete("2023.10: No not available for clients anymore.")]
    Task OnGreet(Contacts_Greet command, CancellationToken cancellationToken);
}

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record Contacts_Touch(
    [property: DataMember, MemoryPackOrder(0)] Session Session,
    [property: DataMember, MemoryPackOrder(1)] ContactId Id
) : ISessionCommand<Unit>;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record Contacts_Change(
    [property: DataMember, MemoryPackOrder(0)] Session Session,
    [property: DataMember, MemoryPackOrder(1)] ContactId Id,
    [property: DataMember, MemoryPackOrder(2)] long? ExpectedVersion,
    [property: DataMember, MemoryPackOrder(3)] Change<Contact> Change
) : ISessionCommand<Contact?>;

[Obsolete("2023.10: No not available for clients anymore.")]
[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record Contacts_Greet(
    [property: DataMember, MemoryPackOrder(0)] Session Session
) : ISessionCommand<Unit>;
