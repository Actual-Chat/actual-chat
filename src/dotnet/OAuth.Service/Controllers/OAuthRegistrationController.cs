using ActualChat.AspNetCore;
using ActualChat.OAuth.Module;
using ActualChat.Resilience;
using ActualLab.Generators;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace ActualChat.OAuth.Controllers;

[ApiController]
public sealed class OAuthRegistrationController(IServiceProvider services) : ControllerBase
{
    private IOpenIddictApplicationManager Applications { get; }
        = services.GetRequiredService<IOpenIddictApplicationManager>();

    private OAuthSettings Settings { get; } = services.GetRequiredService<OAuthSettings>();
    private ILogger Log { get; } = services.LogFor<OAuthRegistrationController>();

    [HttpPost("/oauth/" + OAuthConstants.RegisterRoute)]
    [RateLimitClass(RateLimitClass.Auth)]
    public async Task<ActionResult> Register(
        [FromBody] RegistrationRequest request, CancellationToken cancellationToken)
    {
        var allowInsecure = Settings.AllowInsecureClientMetadata;
        if (request.RedirectUris is not { Length: > 0 })
            return Error("invalid_redirect_uri", "redirect_uris is required.");
        if (request.RedirectUris.Any(u => !OAuthApplications.IsValidRedirectUri(u, allowInsecure)))
            return Error("invalid_redirect_uri",
                "Redirect URIs must be https, or http loopback, and carry no fragment.");

        var authMethod = request.TokenEndpointAuthMethod;
        if (!authMethod.IsNullOrEmpty() && authMethod != ClientAuthenticationMethods.None)
            return Error("invalid_client_metadata",
                "Only public clients (token_endpoint_auth_method=none) are supported.");

        var grantTypes = request.GrantTypes is { Length: > 0 }
            ? request.GrantTypes
            : OAuthApplications.AllowedGrantTypes;
        if (grantTypes.Any(g => !OAuthApplications.AllowedGrantTypes.Contains(g)))
            return Error("invalid_client_metadata",
                "Only authorization_code and refresh_token grant types are supported.");

        var clientId = RandomStringGenerator.Default.Next(24);
        var name = request.ClientName.NullIfEmpty() ?? "Unnamed client";
        var descriptor = OAuthApplications.NewPublicClient(
            clientId, name, request.RedirectUris, OAuthConstants.RegisteredVia.Dcr);
        await Applications.CreateAsync(descriptor, cancellationToken).ConfigureAwait(false);
        Log.LogInformation("DCR: registered client {ClientId} ({ClientName}) from {IP}",
            clientId, name, HttpContext.GetRemoteIPAddress());

        return StatusCode(StatusCodes.Status201Created, new RegistrationResponse {
            ClientId = clientId,
            ClientIdIssuedAt = DateTimeOffset.UtcNow.ToUnixTimeSeconds(),
            ClientName = name,
            RedirectUris = request.RedirectUris,
            GrantTypes = grantTypes,
            ResponseTypes = [ResponseTypes.Code],
            TokenEndpointAuthMethod = ClientAuthenticationMethods.None,
            Scope = OAuthConstants.McpScope + " " + Scopes.OfflineAccess,
        });
    }

    // Private methods

    private ActionResult Error(string error, string description)
        => BadRequest(new { error, error_description = description });

    // Nested types

    public sealed record RegistrationRequest
    {
        [JsonPropertyName("client_name")] public string? ClientName { get; init; }
        [JsonPropertyName("redirect_uris")] public string[]? RedirectUris { get; init; }
        [JsonPropertyName("grant_types")] public string[]? GrantTypes { get; init; }
        [JsonPropertyName("token_endpoint_auth_method")] public string? TokenEndpointAuthMethod { get; init; }
    }

    public sealed record RegistrationResponse
    {
        [JsonPropertyName("client_id")] public required string ClientId { get; init; }
        [JsonPropertyName("client_id_issued_at")] public required long ClientIdIssuedAt { get; init; }
        [JsonPropertyName("client_name")] public required string ClientName { get; init; }
        [JsonPropertyName("redirect_uris")] public required string[] RedirectUris { get; init; }
        [JsonPropertyName("grant_types")] public required string[] GrantTypes { get; init; }
        [JsonPropertyName("response_types")] public required string[] ResponseTypes { get; init; }
        [JsonPropertyName("token_endpoint_auth_method")] public required string TokenEndpointAuthMethod { get; init; }
        [JsonPropertyName("scope")] public required string Scope { get; init; }
    }
}
