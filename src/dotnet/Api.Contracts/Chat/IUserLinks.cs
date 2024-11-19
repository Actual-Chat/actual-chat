namespace ActualChat.Chat;

public interface IUserLinks : IComputeService
{
    [ComputeMethod(MinCacheDuration = 60), RemoteComputeMethod(CacheMode = RemoteComputedCacheMode.Cache, MinCacheDuration = 600)]
    Task<string> ResolveUserLink(UserLinkKind userLinkKind, UserLinkId userLinkId, CancellationToken cancellationToken = default);

    [ComputeMethod(MinCacheDuration = 60)]
    Task<bool> IsUserLinkAvailable(UserLinkId userLinkId, CancellationToken cancellationToken = default);

    [ComputeMethod(MinCacheDuration = 60), RemoteComputeMethod(CacheMode = RemoteComputedCacheMode.Cache, MinCacheDuration = 600)]
    Task<PlaceChatId> GetPlaceChatIdByUserLink(PlaceId placeId, UserLinkId userLinkId, CancellationToken cancellationToken = default);

    [ComputeMethod(MinCacheDuration = 60)]
    Task<bool> IsPlaceChatUserLinkAvailable(PlaceId placeId, UserLinkId userLinkId, CancellationToken cancellationToken = default);
}

public enum UserLinkKind { Chat, Place, User }
