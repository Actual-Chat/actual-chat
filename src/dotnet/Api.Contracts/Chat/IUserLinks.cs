using MemoryPack;

namespace ActualChat.Chat;

public interface IUserLinks : IComputeService
{
    [ComputeMethod(MinCacheDuration = 60), RemoteComputeMethod(CacheMode = RemoteComputedCacheMode.Cache, MinCacheDuration = 600)]
    Task<UserLinkRef> GetUserLinkRef(UserLinkId userLinkId, CancellationToken cancellationToken = default);
    [ComputeMethod(MinCacheDuration = 60), RemoteComputeMethod(CacheMode = RemoteComputedCacheMode.Cache, MinCacheDuration = 600)]
    Task<PlaceChatId?> GetPlaceChatIdByUserLink(PlaceId placeId, UserLinkId userLinkId, CancellationToken cancellationToken = default);
    [ComputeMethod(MinCacheDuration = 60), RemoteComputeMethod(CacheMode = RemoteComputedCacheMode.Cache, MinCacheDuration = 600)]
    Task<UserId?> GetUserIdByUserLink(UserLinkId userLinkId, CancellationToken cancellationToken = default);
}

public enum UserLinkKind { Chat, Place }

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public partial record UserLinkRef(
    [property: DataMember, MemoryPackOrder(0)] UserLinkKind Kind,
    [property: DataMember, MemoryPackOrder(1)] string TargetId)
{
    public static readonly UserLinkRef None = new (UserLinkKind.Chat, "");
    [IgnoreDataMember, MemoryPackIgnore]
    public bool IsNone => TargetId.IsNullOrEmpty();
}
