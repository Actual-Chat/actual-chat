namespace ActualChat.Chat;

public class UserLinks(IServiceProvider services) : IUserLinks
{
    private IUserLinksBackend Backend { get; } = services.GetRequiredService<IUserLinksBackend>();

    public virtual async Task<string> ResolveUserLink(UserLinkKind userLinkKind, UserLinkId userLinkId, CancellationToken cancellationToken)
    {
        var userLink = await Backend.Get(userLinkId, cancellationToken).ConfigureAwait(false);
        if (userLink == null || userLink.Kind != userLinkKind)
            return "";

        return userLink.TargetId;
    }

    public virtual async Task<bool> IsUserLinkAvailable(UserLinkId userLinkId, CancellationToken cancellationToken)
    {
        var userLink = await Backend.Get(userLinkId, cancellationToken).ConfigureAwait(false);
        return userLink is null;
    }
}
