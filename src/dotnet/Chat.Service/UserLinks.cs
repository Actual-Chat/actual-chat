using ActualChat.Users;

namespace ActualChat.Chat;

public class UserLinks(IServiceProvider services) : IUserLinks
{
    private IUserLinksBackend Backend { get; } = services.GetRequiredService<IUserLinksBackend>();
    private IChatsBackend ChatsBackend { get; } = services.GetRequiredService<IChatsBackend>();
    private IAccountsBackend AccountsBackend { get; } = services.GetRequiredService<IAccountsBackend>();

    public virtual async Task<UserLinkRef> GetUserLinkRef(UserLinkId userLinkId, CancellationToken cancellationToken = default)
    {
        var userLink = await Backend.Get(userLinkId, cancellationToken).ConfigureAwait(false);
        if (userLink is null)
            return UserLinkRef.None;

        return new UserLinkRef(userLink.Kind, userLink.TargetId);
    }

    public virtual Task<PlaceChatId?> GetPlaceChatIdByUserLink(PlaceId placeId, UserLinkId userLinkId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(placeId);
        ArgumentNullException.ThrowIfNull(userLinkId);

        return ChatsBackend.GetPlaceChatIdByUserLink(placeId, userLinkId, cancellationToken);
    }

    public virtual Task<UserId?> GetUserIdByUserLink(UserLinkId userLinkId, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(userLinkId);

        return AccountsBackend.GetIdByUserLink(userLinkId, cancellationToken);
    }
}
