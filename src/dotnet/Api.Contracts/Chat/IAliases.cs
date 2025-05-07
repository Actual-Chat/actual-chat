using MemoryPack;

namespace ActualChat.Chat;

public interface IAliases : IComputeService
{
    [ComputeMethod(MinCacheDuration = 60), RemoteComputeMethod(CacheMode = RemoteComputedCacheMode.Cache, MinCacheDuration = 600)]
    Task<AliasTarget?> GetTarget(AliasId aliasId, CancellationToken cancellationToken = default);
    [ComputeMethod(MinCacheDuration = 60), RemoteComputeMethod(CacheMode = RemoteComputedCacheMode.Cache, MinCacheDuration = 600)]
    Task<PlaceChatId?> GetPlaceChatIdByAlias(PlaceId placeId, AliasId aliasId, CancellationToken cancellationToken = default);
    [ComputeMethod(MinCacheDuration = 60), RemoteComputeMethod(CacheMode = RemoteComputedCacheMode.Cache, MinCacheDuration = 600)]
    Task<UserId?> GetUserIdByAlias(AliasId aliasId, CancellationToken cancellationToken = default);
}

public enum AliasKind { Chat, Place }

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public partial record AliasTarget(
    [property: DataMember, MemoryPackOrder(0)] AliasKind Kind,
    [property: DataMember, MemoryPackOrder(1)] string TargetId);
