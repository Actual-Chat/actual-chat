namespace ActualChat.Chat;

public interface IUserLinks : IComputeService
{
    [ComputeMethod(MinCacheDuration = 60), RemoteComputeMethod(CacheMode = RemoteComputedCacheMode.Cache, MinCacheDuration = 600)]
    Task<UserLinkRef> GetUserLinkRef(UserLinkId userLinkId, CancellationToken cancellationToken = default);

    [ComputeMethod(MinCacheDuration = 60), RemoteComputeMethod(CacheMode = RemoteComputedCacheMode.Cache, MinCacheDuration = 600)]
    Task<PlaceChatId> GetPlaceChatIdByUserLink(PlaceId placeId, UserLinkId userLinkId, CancellationToken cancellationToken = default);

    [ComputeMethod(MinCacheDuration = 60), RemoteComputeMethod(CacheMode = RemoteComputedCacheMode.Cache, MinCacheDuration = 600)]
    Task<UserId> GetUserIdByUserLink(UserLinkId userLinkId, CancellationToken cancellationToken = default);
}

public enum UserLinkKind { Chat, Place }

public record UserLinkRef(UserLinkKind Kind, string TargetId)
{
    public static readonly UserLinkRef None = new (UserLinkKind.Chat, "");
    public bool IsNone => TargetId.IsNullOrEmpty();
}
