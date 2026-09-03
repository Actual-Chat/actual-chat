namespace ActualChat.UI.Blazor.App.Services;

/// <summary>
/// The inverse of <see cref="Links.User(UserId)"/>: the user a <c>/u/...</c> link points to.
/// </summary>
public static class UserLinks
{
    private const string Prefix = "/u/";

    public static Task<UserId?> GetUserId(IAliases aliases, LocalUrl url, CancellationToken cancellationToken)
    {
        var value = url.Value;
        if (!value.StartsWith(Prefix))
            return Task.FromResult<UserId?>(null);

        var route = value[Prefix.Length..];
        var queryStart = route.IndexOfAny(['?', '#']);
        if (queryStart >= 0)
            route = route[..queryStart];
        return GetUserIdByRoute(aliases, route.UrlDecode(), cancellationToken);
    }

    // The route is the part after /u/, as a page gets it
    public static async Task<UserId?> GetUserIdByRoute(
        IAliases aliases, string? route, CancellationToken cancellationToken)
    {
        if (route.IsNullOrEmpty())
            return null;

        if (!route.StartsWith(Links.AliasPrefix))
            return UserId.TryParse(route);

        if (!AliasId.TryParse(route.Substring(1), out var aliasId))
            return null;

        return await aliases.GetUserIdByAlias(aliasId, cancellationToken).ConfigureAwait(false);
    }
}
