using System.Security.Claims;
using Microsoft.AspNetCore;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Mvc;
using Microsoft.IdentityModel.Tokens;
using OpenIddict.Abstractions;
using OpenIddict.Server.AspNetCore;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace ActualChat.OAuth.Controllers;

// OpenIddict validates every request (client, redirect_uri, PKCE, grant type, code / refresh token)
// before these actions run, so they only decide what to sign in and which claims to carry.

/// <summary>
/// Authorization-code + refresh-token endpoints for MCP clients: tokens are minted against
/// the grant's backing <see cref="SessionKind.OAuth"/> session, whose id rides in the "sid" claim.
/// </summary>
public sealed class OAuthController(IServiceProvider services) : ControllerBase
{
    private IServiceProvider Services { get; } = services;
    private IAccounts Accounts { get; } = services.GetRequiredService<IAccounts>();
    private ISessionsBackend SessionsBackend { get; } = services.GetRequiredService<ISessionsBackend>();
    private OAuthGrants Grants { get; } = services.GetRequiredService<OAuthGrants>();
    private IOpenIddictApplicationManager Applications { get; }
        = services.GetRequiredService<IOpenIddictApplicationManager>();
    private IOpenIddictAuthorizationManager Authorizations { get; }
        = services.GetRequiredService<IOpenIddictAuthorizationManager>();
    private UrlMapper UrlMapper { get; } = services.UrlMapper();
    private ILogger Log { get; } = services.LogFor<OAuthController>();

    [HttpGet("/oauth/" + OAuthConstants.AuthorizeRoute), HttpPost("/oauth/" + OAuthConstants.AuthorizeRoute)]
    public async Task<IActionResult> Authorize(CancellationToken cancellationToken)
    {
        var request = HttpContext.GetOpenIddictServerRequest()
            ?? throw StandardError.Internal("OpenIddict request is missing.");
        var session = HttpContext.TryGetSessionFromCookie();
        var account = session is null ? null : await Accounts.GetOwn(session, cancellationToken).ConfigureAwait(false);
        if (account is null || account.IsGuest)
            return RedirectToConsent();

        if (Request.Query.ContainsKey(OAuthConstants.DenyParameter))
            return Reject(Errors.AccessDenied, "The user denied the request.");

        var application = await Applications.FindByClientIdAsync(request.ClientId!, cancellationToken)
            .ConfigureAwait(false)
            ?? throw StandardError.Internal("Client was validated by OpenIddict but is missing.");
        var applicationId = (await Applications.GetIdAsync(application, cancellationToken).ConfigureAwait(false))!;
        var scopes = request.GetScopes();
        var authorization = await Grants.FindAuthorization(account.Id, applicationId, scopes, cancellationToken)
            .ConfigureAwait(false);
        if (authorization is null)
            return RedirectToConsent();

        var sessionId = await Grants.GetSessionId(Services, authorization, cancellationToken).ConfigureAwait(false)
            ?? throw StandardError.Internal("Authorization has no backing session.");
        if (!await IsSessionActive(sessionId, cancellationToken).ConfigureAwait(false))
            return RedirectToConsent();

        var identity = new ClaimsIdentity(
            TokenValidationParameters.DefaultAuthenticationType, Claims.Name, Claims.Role);
        identity.SetClaim(Claims.Subject, account.Id.Value)
            .SetClaim(Claims.Name, account.Avatar.Name)
            .SetClaim(OAuthConstants.SessionIdClaim, sessionId);
        identity.SetScopes(scopes);
        var resources = request.GetResources();
        identity.SetResources(resources.IsDefaultOrEmpty
            ? [UrlMapper.ToAbsolute(OAuthConstants.McpResourcePath)]
            : resources);
        identity.SetAuthorizationId(await Authorizations.GetIdAsync(authorization, cancellationToken)
            .ConfigureAwait(false));
        identity.SetDestinations(GetDestinations);
        Log.LogInformation("Authorize: user {UserId} client {ClientId}", account.Id, request.ClientId);
        return SignIn(new ClaimsPrincipal(identity), OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);

        IActionResult RedirectToConsent() {
            var query = Request.QueryString.HasValue ? Request.QueryString.Value : "";
            return Redirect(OAuthConstants.ConsentPath + query);
        }
    }

    [HttpPost("/oauth/" + OAuthConstants.TokenRoute)]
    public async Task<IActionResult> Exchange(CancellationToken cancellationToken)
    {
        var request = HttpContext.GetOpenIddictServerRequest()
            ?? throw StandardError.Internal("OpenIddict request is missing.");
        if (!request.IsAuthorizationCodeGrantType() && !request.IsRefreshTokenGrantType())
            return Reject(Errors.UnsupportedGrantType, "Only authorization_code and refresh_token are supported.");

        // The principal comes from the code / refresh token; OpenIddict has already validated the token itself,
        // this re-checks the grant behind it: an OpenIddict authorization that's still Valid + a live session.
        var result = await HttpContext.AuthenticateAsync(OpenIddictServerAspNetCoreDefaults.AuthenticationScheme)
            .ConfigureAwait(false);
        var principal = result.Principal ?? throw StandardError.Internal("Token principal is missing.");
        var sessionId = principal.GetClaim(OAuthConstants.SessionIdClaim);
        if (sessionId is null || !await IsGrantValid(principal, cancellationToken).ConfigureAwait(false))
            return Reject(Errors.InvalidGrant, "The grant was revoked.");
        if (!await IsSessionActive(sessionId, cancellationToken).ConfigureAwait(false))
            return Reject(Errors.InvalidGrant, "The grant has expired.");

        if (request.IsRefreshTokenGrantType())
            await Grants.TouchSession(sessionId, cancellationToken).ConfigureAwait(false);
        principal.SetDestinations(GetDestinations);
        return SignIn(principal, OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);
    }

    // Private methods

    private async Task<bool> IsGrantValid(ClaimsPrincipal principal, CancellationToken cancellationToken)
    {
        var authorizationId = principal.GetAuthorizationId();
        if (authorizationId is null)
            return false;

        var authorization = await Authorizations.FindByIdAsync(authorizationId, cancellationToken)
            .ConfigureAwait(false);
        if (authorization is null)
            return false;

        var status = await Authorizations.GetStatusAsync(authorization, cancellationToken).ConfigureAwait(false);
        return status == Statuses.Valid;
    }

    private async Task<bool> IsSessionActive(string sessionId, CancellationToken cancellationToken)
    {
        var sessionInfo = await SessionsBackend.Get(new Session(sessionId), cancellationToken).ConfigureAwait(false);
        return sessionInfo is { IsActive: true, UserId: not null };
    }

    private ForbidResult Reject(string error, string description)
        => Forbid(
            new AuthenticationProperties(new Dictionary<string, string?> {
                [OpenIddictServerAspNetCoreConstants.Properties.Error] = error,
                [OpenIddictServerAspNetCoreConstants.Properties.ErrorDescription] = description,
            }),
            OpenIddictServerAspNetCoreDefaults.AuthenticationScheme);

    private static IEnumerable<string> GetDestinations(Claim claim)
        // Every claim goes to the access token only: no identity token is issued
        => [Destinations.AccessToken];
}
