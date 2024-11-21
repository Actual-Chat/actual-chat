using ActualChat.Users;

namespace ActualChat.Chat;

public class UserLinks(IServiceProvider services) : IUserLinks
{
    private IUserLinksBackend Backend { get; } = services.GetRequiredService<IUserLinksBackend>();
    private IChatsBackend ChatsBackend { get; } = services.GetRequiredService<IChatsBackend>();
    private IAccountsBackend AccountsBackend { get; } = services.GetRequiredService<IAccountsBackend>();

    public virtual async Task<UserLinkRef> GetUserLinkRef(UserLinkId userLinkId, CancellationToken cancellationToken = default)
    {
        if (userLinkId.IsNone)
            throw new ArgumentOutOfRangeException(nameof(userLinkId));

        var userLink = await Backend.Get(userLinkId, cancellationToken).ConfigureAwait(false);
        if (userLink is null)
            return UserLinkRef.None;

        return new UserLinkRef(userLink.Kind, userLink.TargetId);
    }

    public virtual Task<PlaceChatId> GetPlaceChatIdByUserLink(PlaceId placeId, UserLinkId userLinkId, CancellationToken cancellationToken)
    {
        if (placeId.IsNone)
            throw new ArgumentOutOfRangeException(nameof(placeId));
        if (userLinkId.IsNone)
            throw new ArgumentOutOfRangeException(nameof(userLinkId));

        return ChatsBackend.GetPlaceChatIdByUserLink(placeId, userLinkId.ToLower(), cancellationToken);
    }

    public virtual Task<UserId> GetUserIdByUserLink(UserLinkId userLinkId, CancellationToken cancellationToken)
    {
        if (userLinkId.IsNone)
            throw new ArgumentOutOfRangeException(nameof(userLinkId));

        return AccountsBackend.GetIdByUserLink(userLinkId, cancellationToken);
    }
}
