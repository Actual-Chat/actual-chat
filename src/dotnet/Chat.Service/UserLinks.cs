using ActualChat.Users;

namespace ActualChat.Chat;

public class UserLinks(IServiceProvider services) : IUserLinks
{
    private IUserLinksBackend Backend { get; } = services.GetRequiredService<IUserLinksBackend>();
    private IChatsBackend ChatsBackend { get; } = services.GetRequiredService<IChatsBackend>();
    private IAccountsBackend AccountsBackend { get; } = services.GetRequiredService<IAccountsBackend>();

    public virtual async Task<ResolvedUserLinkResult> ResolveUserLink(UserLinkId userLinkId, CancellationToken cancellationToken = default)
    {
        if (userLinkId.IsNone)
            throw new ArgumentOutOfRangeException(nameof(userLinkId));

        var userLink = await Backend.Get(userLinkId, cancellationToken).ConfigureAwait(false);
        if (userLink is null)
            return ResolvedUserLinkResult.None;

        return new ResolvedUserLinkResult(userLink.Kind, userLink.TargetId);
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

        return ChatsBackend.GetPlaceChatIdByUserLink(placeId, userLinkId.ToLower(), cancellationToken);
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

    public virtual Task<UserId> ResolveAccountUserLink(UserLinkId userLinkId, CancellationToken cancellationToken)
    {
        if (userLinkId.IsNone)
            throw new ArgumentOutOfRangeException(nameof(userLinkId));

        return AccountsBackend.GetIdByUserLink(userLinkId, cancellationToken);
    }

    public virtual async Task<bool> IsUserLinkAvailableForAccount(UserLinkId userLinkId, CancellationToken cancellationToken)
    {
        if (userLinkId.IsNone)
            throw new ArgumentOutOfRangeException(nameof(userLinkId));

        var userId = await ResolveAccountUserLink(userLinkId, cancellationToken).ConfigureAwait(false);
        return userId.IsNone;
    }
}
