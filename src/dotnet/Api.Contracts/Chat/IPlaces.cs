﻿using MemoryPack;

namespace ActualChat.Chat;

public interface IPlaces : IComputeService
{
    [ComputeMethod(MinCacheDuration = 60), RemoteComputeMethod(CacheMode = RemoteComputedCacheMode.Cache)]
    Task<Place?> Get(Session session, PlaceId placeId, CancellationToken cancellationToken);
    [ComputeMethod(MinCacheDuration = 60), RemoteComputeMethod(CacheMode = RemoteComputedCacheMode.Cache)]
    Task<PlaceRules> GetRules(Session session, PlaceId placeId, CancellationToken cancellationToken);
    [ComputeMethod(MinCacheDuration = 60), RemoteComputeMethod(CacheMode = RemoteComputedCacheMode.Cache)]
    Task<ChatId> GetWelcomeChatId(Session session, PlaceId placeId, CancellationToken cancellationToken);

    [ComputeMethod]
    Task<ApiArray<UserId>> ListUserIds(Session session, PlaceId placeId, CancellationToken cancellationToken);
    [ComputeMethod]
    Task<ApiArray<AuthorId>> ListAuthorIds(Session session, PlaceId placeId, CancellationToken cancellationToken);
    [ComputeMethod]
    Task<ApiArray<AuthorId>> ListOwnerIds(Session session, PlaceId placeId, CancellationToken cancellationToken);

    [ComputeMethod, RemoteComputeMethod(CacheMode = RemoteComputedCacheMode.Cache)]
    Task<AuthorFull?> GetOwn(Session session, PlaceId placeId, CancellationToken cancellationToken);
    [ComputeMethod, RemoteComputeMethod(CacheMode = RemoteComputedCacheMode.Cache)]
    Task<Author?> Get(Session session, PlaceId placeId, AuthorId authorId, CancellationToken cancellationToken);

    // Commands

    [CommandHandler]
    Task<Place> OnChange(Places_Change command, CancellationToken cancellationToken);
    [CommandHandler]
    Task OnJoin(Places_Join command, CancellationToken cancellationToken);
    [CommandHandler]
    Task OnInvite(Places_Invite command, CancellationToken cancellationToken);
    [CommandHandler]
    Task OnExclude(Places_Exclude command, CancellationToken cancellationToken);
    [CommandHandler]
    Task OnRestore(Places_Restore command, CancellationToken cancellationToken);
    [CommandHandler]
    Task OnPromoteToOwner(Places_PromoteToOwner command, CancellationToken cancellationToken);
    [CommandHandler]
    [Obsolete($"2024.06: Use '{nameof(Places_Change)}' with Change.Remove instead.")]
    Task OnDelete(Places_Delete command, CancellationToken cancellationToken);
    [CommandHandler]
    Task OnLeave(Places_Leave command, CancellationToken cancellationToken);
}

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record Places_Change(
    [property: DataMember, MemoryPackOrder(0)] Session Session,
    [property: DataMember, MemoryPackOrder(1)] PlaceId PlaceId,
    [property: DataMember, MemoryPackOrder(2)] long? ExpectedVersion,
    [property: DataMember, MemoryPackOrder(3)] Change<PlaceDiff> Change
) : ISessionCommand<Place>, IApiCommand;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record Places_Join(
    [property: DataMember, MemoryPackOrder(0)] Session Session,
    [property: DataMember, MemoryPackOrder(1)] PlaceId PlaceId,
    [property: DataMember, MemoryPackOrder(2)] Symbol AvatarId = default
) : ISessionCommand<Unit>, IApiCommand;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record Places_Invite(
    [property: DataMember, MemoryPackOrder(0)] Session Session,
    [property: DataMember, MemoryPackOrder(1)] PlaceId PlaceId,
    [property: DataMember, MemoryPackOrder(2)] UserId[] UserIds
) : ISessionCommand<Unit>, IApiCommand;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record Places_Exclude(
    [property: DataMember, MemoryPackOrder(0)] Session Session,
    [property: DataMember, MemoryPackOrder(1)] AuthorId AuthorId
) : ISessionCommand<Unit>, IApiCommand;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record Places_Restore(
    [property: DataMember, MemoryPackOrder(0)] Session Session,
    [property: DataMember, MemoryPackOrder(1)] AuthorId AuthorId
) : ISessionCommand<Unit>, IApiCommand;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record Places_PromoteToOwner(
    [property: DataMember, MemoryPackOrder(0)] Session Session,
    [property: DataMember, MemoryPackOrder(1)] AuthorId AuthorId
) : ISessionCommand<Unit>, IApiCommand;

[Obsolete($"2024.06: Use '{nameof(Places_Change)}' with Change.Remove instead.")]
[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record Places_Delete(
    [property: DataMember, MemoryPackOrder(0)] Session Session,
    [property: DataMember, MemoryPackOrder(1)] PlaceId PlaceId
) : ISessionCommand<Unit>, IApiCommand;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record Places_Leave(
    [property: DataMember, MemoryPackOrder(0)] Session Session,
    [property: DataMember, MemoryPackOrder(1)] PlaceId PlaceId
) : ISessionCommand<Unit>, IApiCommand;
