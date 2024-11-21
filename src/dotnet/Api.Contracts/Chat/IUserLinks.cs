namespace ActualChat.Chat;

public interface IUserLinks : IComputeService
{
    [ComputeMethod(MinCacheDuration = 60), RemoteComputeMethod(CacheMode = RemoteComputedCacheMode.Cache, MinCacheDuration = 600)]
    Task<ResolvedUserLinkResult> ResolveUserLink(UserLinkId userLinkId, CancellationToken cancellationToken = default);

    [ComputeMethod(MinCacheDuration = 60)]
    Task<bool> IsUserLinkAvailable(UserLinkId userLinkId, CancellationToken cancellationToken = default);

    [ComputeMethod(MinCacheDuration = 60), RemoteComputeMethod(CacheMode = RemoteComputedCacheMode.Cache, MinCacheDuration = 600)]
    Task<PlaceChatId> GetPlaceChatIdByUserLink(PlaceId placeId, UserLinkId userLinkId, CancellationToken cancellationToken = default);

    [ComputeMethod(MinCacheDuration = 60)]
    Task<bool> IsPlaceChatUserLinkAvailable(PlaceId placeId, UserLinkId userLinkId, CancellationToken cancellationToken = default);

    [ComputeMethod(MinCacheDuration = 60), RemoteComputeMethod(CacheMode = RemoteComputedCacheMode.Cache, MinCacheDuration = 600)]
    Task<UserId> ResolveAccountUserLink(UserLinkId userLinkId, CancellationToken cancellationToken = default);

    [ComputeMethod(MinCacheDuration = 60)]
    Task<bool> IsUserLinkAvailableForAccount(UserLinkId userLinkId, CancellationToken cancellationToken = default);
}

public enum UserLinkKind { Chat, Place }

public record ResolvedUserLinkResult(UserLinkKind Kind, string TargetId)
{
    public static readonly ResolvedUserLinkResult None = new (UserLinkKind.Chat, "");
    public bool IsNone => TargetId.IsNullOrEmpty();
}
