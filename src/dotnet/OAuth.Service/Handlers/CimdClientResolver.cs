using ActualChat.OAuth.Module;
using Microsoft.EntityFrameworkCore;
using OpenIddict.Abstractions;
using OpenIddict.Server;
using static OpenIddict.Abstractions.OpenIddictConstants;
using static OpenIddict.Server.OpenIddictServerEvents;

namespace ActualChat.OAuth.Handlers;

/// <summary>
/// Client ID Metadata Documents (MCP auth spec 2025-11-25): a <c>client_id</c> that is an https URL names
/// a JSON document describing the client. Runs before OpenIddict's client lookup and registers the
/// application just in time, so the built-in validation then finds it like any DCR client.
/// </summary>
public sealed class CimdClientResolver(IServiceProvider services)
    : IOpenIddictServerHandler<ValidateAuthorizationRequestContext>
{
    public const string HttpClientName = "OAuth.Cimd";
    public const int MaxDocumentLength = 64 * 1024;

    // ValidateClientIdParameter is the presence check; the existence check runs inside ValidateAuthentication,
    // the next handler in this pipeline, so this lands between the two
    public static readonly OpenIddictServerHandlerDescriptor Descriptor
        = OpenIddictServerHandlerDescriptor.CreateBuilder<ValidateAuthorizationRequestContext>()
            .UseScopedHandler<CimdClientResolver>()
            .SetOrder(OpenIddictServerHandlers.Authentication.ValidateClientIdParameter.Descriptor.Order + 500)
            .Build();

    private IOpenIddictApplicationManager Applications { get; }
        = services.GetRequiredService<IOpenIddictApplicationManager>();
    private IHttpClientFactory HttpClientFactory { get; } = services.GetRequiredService<IHttpClientFactory>();
    private OAuthSettings Settings { get; } = services.GetRequiredService<OAuthSettings>();
    private MomentClockSet Clocks { get; } = services.Clocks();
    private ILogger Log => field ??= services.LogFor(GetType());

    public async ValueTask HandleAsync(ValidateAuthorizationRequestContext context)
    {
        var clientId = context.ClientId;
        if (clientId.IsNullOrEmpty() || !Uri.TryCreate(clientId, UriKind.Absolute, out var uri))
            return;
        if (uri.Scheme != Uri.UriSchemeHttps && uri.Scheme != Uri.UriSchemeHttp)
            return;
        if (uri.Scheme == Uri.UriSchemeHttp && !Settings.AllowInsecureClientMetadata) {
            context.Reject(Errors.InvalidClient, "Client metadata documents must be served over https.");
            return;
        }

        var cancellationToken = context.CancellationToken;
        var application = await Applications.FindByClientIdAsync(clientId, cancellationToken).ConfigureAwait(false);
        if (application is not null && !await IsStale(application, cancellationToken).ConfigureAwait(false))
            return;

        var document = await Fetch(uri, cancellationToken).ConfigureAwait(false);
        if (document is null || document.ClientId != clientId) {
            context.Reject(Errors.InvalidClient,
                "The client metadata document could not be fetched or names a different client_id.");
            return;
        }
        if (document.RedirectUris is not { Length: > 0 }
            || document.RedirectUris.Any(u => !OAuthApplications.IsValidRedirectUri(u))) {
            context.Reject(Errors.InvalidClient, "The client metadata document has no acceptable redirect_uris.");
            return;
        }

        var descriptor = OAuthApplications.NewPublicClient(
            clientId,
            document.ClientName.NullIfEmpty() ?? uri.Host,
            document.RedirectUris,
            OAuthConstants.RegisteredVia.Cimd);
        descriptor.Properties[OAuthConstants.Properties.CimdFetchedAt]
            = JsonSerializer.SerializeToElement(Clocks.SystemClock.Now.ToDateTime());
        if (application is not null)
            await Applications.UpdateAsync(application, descriptor, cancellationToken).ConfigureAwait(false);
        else
            await Create(descriptor, cancellationToken).ConfigureAwait(false);
        Log.LogInformation("CIMD: registered client {ClientId} ({ClientName})", clientId, descriptor.DisplayName);
    }

    // Private methods

    private async Task<bool> IsStale(object application, CancellationToken cancellationToken)
    {
        var properties = await Applications.GetPropertiesAsync(application, cancellationToken).ConfigureAwait(false);
        if (!properties.TryGetValue(OAuthConstants.Properties.CimdFetchedAt, out var fetchedAt))
            return true;

        return fetchedAt.GetDateTime() + Settings.CimdCacheAge < Clocks.SystemClock.Now.ToDateTime();
    }

    private async Task<ClientMetadata?> Fetch(Uri uri, CancellationToken cancellationToken)
    {
        // The named client caps the body at MaxDocumentLength, so an oversized document throws here
        try {
            using var http = HttpClientFactory.CreateClient(HttpClientName);
            using var response = await http.GetAsync(uri, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                return null;

            var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);
            return JsonSerializer.Deserialize<ClientMetadata>(bytes);
        }
        catch (Exception e) when (e is not OperationCanceledException) {
            Log.LogWarning(e, "CIMD: failed to fetch {Uri}", uri);
            return null;
        }
    }

    private async Task Create(OpenIddictApplicationDescriptor descriptor, CancellationToken cancellationToken)
    {
        // Two first-time requests for the same client can race on the unique client_id; the loser's
        // document is the same one, so the winner's row serves it
        try {
            await Applications.CreateAsync(descriptor, cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException) {
            var existing = await Applications.FindByClientIdAsync(descriptor.ClientId!, cancellationToken)
                .ConfigureAwait(false);
            if (existing is null)
                throw;
        }
    }

    // Nested types

    private sealed record ClientMetadata
    {
        [JsonPropertyName("client_id")] public string? ClientId { get; init; }
        [JsonPropertyName("client_name")] public string? ClientName { get; init; }
        [JsonPropertyName("redirect_uris")] public string[]? RedirectUris { get; init; }
    }
}
