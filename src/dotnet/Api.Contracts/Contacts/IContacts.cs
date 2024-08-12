using MemoryPack;

namespace ActualChat.Contacts;

public interface IContacts : IComputeService
{
    [ComputeMethod, RemoteComputeMethod(CacheMode = RemoteComputedCacheMode.Cache, MinCacheDuration = 600)]
    Task<Contact?> Get(Session session, ContactId contactId, CancellationToken cancellationToken);
    [ComputeMethod, RemoteComputeMethod(CacheMode = RemoteComputedCacheMode.Cache, MinCacheDuration = 600)]
    Task<Contact?> GetForChat(Session session, ChatId chatId, CancellationToken cancellationToken);
    [ComputeMethod(MinCacheDuration = 300)]
    [Obsolete("2024.04: Use overload that takes placeId parameter instead.")]
    Task<ApiArray<ContactId>> ListIds(Session session, CancellationToken cancellationToken);
    [ComputeMethod(MinCacheDuration = 300), RemoteComputeMethod(CacheMode = RemoteComputedCacheMode.Cache, MinCacheDuration = 600)]
    Task<ApiArray<PlaceId>> ListPlaceIds(Session session, CancellationToken cancellationToken);
    [ComputeMethod(MinCacheDuration = 300), RemoteComputeMethod(CacheMode = RemoteComputedCacheMode.Cache, MinCacheDuration = 600)]
    Task<ApiArray<ContactId>> ListIds(Session session, PlaceId placeId, CancellationToken cancellationToken);

    [CommandHandler]
    Task<Contact?> OnChange(Contacts_Change command, CancellationToken cancellationToken);
    [CommandHandler]
    Task OnTouch(Contacts_Touch command, CancellationToken cancellationToken);
    [CommandHandler, Obsolete("2023.10: Not available for clients anymore.")]
    Task OnGreet(Contacts_Greet command, CancellationToken cancellationToken);
}

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record Contacts_Touch(
    [property: DataMember, MemoryPackOrder(0)] Session Session,
    [property: DataMember, MemoryPackOrder(1)] ContactId Id
) : ISessionCommand<Unit>, IApiCommand;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record Contacts_Change(
    [property: DataMember, MemoryPackOrder(0)] Session Session,
    [property: DataMember, MemoryPackOrder(1)] ContactId Id,
    [property: DataMember, MemoryPackOrder(2)] long? ExpectedVersion,
    [property: DataMember, MemoryPackOrder(3)] Change<Contact> Change
) : ISessionCommand<Contact?>, IApiCommand;

[Obsolete("2023.10: No not available for clients anymore.")]
[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record Contacts_Greet(
    [property: DataMember, MemoryPackOrder(0)] Session Session
) : ISessionCommand<Unit>, IApiCommand;
