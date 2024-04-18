using MemoryPack;

namespace ActualChat.Users;

public interface IAvatars : IComputeService
{
    [ComputeMethod(MinCacheDuration = 10), ClientComputeMethod(ClientCacheMode = ClientCacheMode.Cache)]
    Task<AvatarFull?> GetOwn(Session session, Symbol avatarId, CancellationToken cancellationToken);
    [ComputeMethod(MinCacheDuration = 10), ClientComputeMethod(ClientCacheMode = ClientCacheMode.Cache)]
    Task<Avatar?> Get(Session session, Symbol avatarId, CancellationToken cancellationToken);
    [ComputeMethod]
    Task<ApiArray<Symbol>> ListOwnAvatarIds(Session session, CancellationToken cancellationToken);

    [CommandHandler]
    Task<AvatarFull> OnChange(Avatars_Change command, CancellationToken cancellationToken);
    [CommandHandler]
    Task OnSetDefault(Avatars_SetDefault command, CancellationToken cancellationToken);
}

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record Avatars_Change(
    [property: DataMember, MemoryPackOrder(0)] Session Session,
    [property: DataMember, MemoryPackOrder(1)] Symbol AvatarId,
    [property: DataMember, MemoryPackOrder(2)] long? ExpectedVersion,
    [property: DataMember, MemoryPackOrder(3)] Change<AvatarFull> Change
) : ISessionCommand<AvatarFull>, IApiCommand;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
// ReSharper disable once InconsistentNaming
public sealed partial record Avatars_SetDefault(
    [property: DataMember, MemoryPackOrder(0)] Session Session,
    [property: DataMember, MemoryPackOrder(1)] Symbol AvatarId
) : ISessionCommand<Unit>, IApiCommand;
