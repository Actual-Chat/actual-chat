using OpenIddict.Abstractions;
using OpenIddict.Server;
using static OpenIddict.Abstractions.OpenIddictConstants;
using static OpenIddict.Server.OpenIddictServerEvents;

namespace ActualChat.OAuth.Handlers;

/// <summary>
/// Runs once OpenIddict has answered a <c>/oauth/revoke</c> call: when the authorization behind the
/// presented refresh token has no valid refresh token left, the client can never come back, so the
/// grant and its backing session go too — otherwise the user's connected-apps list would show a ghost.
/// </summary>
public sealed class RevocationSessionHandler(IServiceProvider services)
    : IOpenIddictServerHandler<ApplyRevocationResponseContext>
{
    // After NormalizeErrorResponse, so an invalid_token outcome reads as success: that's also how a
    // redeemed refresh token ends (OpenIddict's reuse detection revoked the whole grant's tokens first,
    // which is exactly the state this handler must act on). Before the ASP.NET Core handlers at 100_000,
    // which write the response and stop the pipeline.
    public static readonly OpenIddictServerHandlerDescriptor Descriptor
        = OpenIddictServerHandlerDescriptor.CreateBuilder<ApplyRevocationResponseContext>()
            .UseScopedHandler<RevocationSessionHandler>()
            .SetOrder(OpenIddictServerHandlers.Revocation.NormalizeErrorResponse.Descriptor.Order + 500)
            .Build();

    private IOpenIddictTokenManager Tokens { get; } = services.GetRequiredService<IOpenIddictTokenManager>();

    private IOpenIddictAuthorizationManager Authorizations { get; }
        = services.GetRequiredService<IOpenIddictAuthorizationManager>();

    private OAuthGrants Grants { get; } = services.GetRequiredService<OAuthGrants>();

    public async ValueTask HandleAsync(ApplyRevocationResponseContext context)
    {
        // A remaining error means the caller never authenticated as a client, so nothing was revoked.
        // Only reference (refresh) tokens resolve by their client-visible value; a JWT access token
        // never does, and revoking one alone leaves the refresh token — and the grant — in place.
        if (!context.Error.IsNullOrEmpty() || context.Request?.Token is not { Length: > 0 } reference)
            return;

        var cancellationToken = context.CancellationToken;
        var token = await Tokens.FindByReferenceIdAsync(reference, cancellationToken).ConfigureAwait(false);
        if (token is null)
            return;

        var authorizationId = await Tokens.GetAuthorizationIdAsync(token, cancellationToken).ConfigureAwait(false);
        if (authorizationId.IsNullOrEmpty())
            return;

        var authorizationTokens = Tokens.FindByAuthorizationIdAsync(authorizationId, cancellationToken);
        await foreach (var t in authorizationTokens.ConfigureAwait(false)) {
            if (await Tokens.GetTypeAsync(t, cancellationToken).ConfigureAwait(false) != TokenTypeHints.RefreshToken)
                continue;
            if (await Tokens.GetStatusAsync(t, cancellationToken).ConfigureAwait(false) == Statuses.Valid)
                return;
        }

        var authorization = await Authorizations.FindByIdAsync(authorizationId, cancellationToken)
            .ConfigureAwait(false);
        if (authorization is null)
            return;
        if (await Authorizations.GetStatusAsync(authorization, cancellationToken).ConfigureAwait(false)
            != Statuses.Valid)
            return;

        await Grants.Revoke(services, authorization, cancellationToken).ConfigureAwait(false);
    }
}
