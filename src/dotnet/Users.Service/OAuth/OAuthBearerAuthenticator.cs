using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using OpenIddict.Abstractions;
using OpenIddict.Validation.AspNetCore;

namespace ActualChat.OAuth;

/// <summary>
/// Turns an OpenIddict-issued access token into the grant's backing <see cref="Session"/>:
/// signature/expiry via OpenIddict validation (in-process), then the session row — which is
/// also the revocation check. Reusable by any resource endpoint, not only MCP.
/// </summary>
public sealed class OAuthBearerAuthenticator(IServiceProvider services)
{
    private ISessionsBackend SessionsBackend { get; } = services.GetRequiredService<ISessionsBackend>();
    private UrlMapper UrlMapper { get; } = services.UrlMapper();

    public async Task<Session?> TryGetSession(
        HttpContext httpContext, IEnumerable<string> resourcePaths, CancellationToken cancellationToken)
    {
        var result = await httpContext.AuthenticateAsync(OpenIddictValidationAspNetCoreDefaults.AuthenticationScheme)
            .ConfigureAwait(false);
        if (!result.Succeeded || result.Principal is not { } principal)
            return null;

        if (!resourcePaths.Any(path => principal.HasAudience(UrlMapper.ToAbsolute(path))))
            return null;

        var session = SessionExt.NewValidOrNull(principal.GetClaim(OAuthConstants.SessionIdClaim));
        if (session is null || session.Kind != SessionKind.OAuth)
            return null;

        var info = await SessionsBackend.Get(session, cancellationToken).ConfigureAwait(false);
        if (info is null || !info.IsActive || info.UserId is not { IsGuest: false })
            return null;

        return session;
    }
}
