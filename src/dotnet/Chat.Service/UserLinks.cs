namespace ActualChat.Chat;

public class UserLinks(IServiceProvider services) : IUserLinks
{
    private IUserLinksBackend Backend { get; } = services.GetRequiredService<IUserLinksBackend>();
    private IChatsBackend ChatsBackend { get; } = services.GetRequiredService<IChatsBackend>();

    public virtual async Task<string> ResolveUserLink(UserLinkKind userLinkKind, UserLinkId userLinkId, CancellationToken cancellationToken)
    {
        if (userLinkId.IsNone)
            throw new ArgumentOutOfRangeException(nameof(userLinkId));

        var userLink = await Backend.Get(userLinkId, cancellationToken).ConfigureAwait(false);
        if (userLink == null || userLink.Kind != userLinkKind)
            return "";

        return userLink.TargetId;
    }

    public virtual async Task<bool> IsUserLinkAvailable(UserLinkId userLinkId, CancellationToken cancellationToken)
    {
        if (userLinkId.IsNone)
            throw new ArgumentOutOfRangeException(nameof(userLinkId));

        var userLink = await Backend.Get(userLinkId, cancellationToken).ConfigureAwait(false);
        return userLink is null;
    }

    public virtual Task<PlaceChatId> GetPlaceChatIdByUserLink(PlaceId placeId, UserLinkId userLinkId, CancellationToken cancellationToken)
    {
        if (placeId.IsNone)
            throw new ArgumentOutOfRangeException(nameof(placeId));
        if (userLinkId.IsNone)
            throw new ArgumentOutOfRangeException(nameof(userLinkId));

        return ChatsBackend.GetPlaceChatIdByUserLink(placeId, userLinkId, cancellationToken);
    }

    public virtual async Task<bool> IsPlaceChatUserLinkAvailable(PlaceId placeId, UserLinkId userLinkId, CancellationToken cancellationToken)
    {
        if (placeId.IsNone)
            throw new ArgumentOutOfRangeException(nameof(placeId));
        if (userLinkId.IsNone)
            throw new ArgumentOutOfRangeException(nameof(userLinkId));

        var chatId = await GetPlaceChatIdByUserLink(placeId, userLinkId, cancellationToken).ConfigureAwait(false);
        return chatId.IsNone;
    }
}
