using OpenIddict.Abstractions;
using static OpenIddict.Abstractions.OpenIddictConstants;

namespace ActualChat.OAuth;

public static class OAuthApplications
{
    public static readonly string[] AllowedGrantTypes = [GrantTypes.AuthorizationCode, GrantTypes.RefreshToken];

    public static bool IsValidRedirectUri(string value, bool allowInsecure)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || !uri.Fragment.IsNullOrEmpty())
            return false;

        return uri.Scheme == Uri.UriSchemeHttps
            || IsLoopback(uri)
            || (allowInsecure && uri.Scheme == Uri.UriSchemeHttp);
    }

    public static bool IsLoopback(Uri uri)
        => uri.Scheme == Uri.UriSchemeHttp
            && (uri.IsLoopback || string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase));

    public static OpenIddictApplicationDescriptor NewPublicClient(
        string clientId, string displayName, IEnumerable<string> redirectUris, string registeredVia)
    {
        var descriptor = new OpenIddictApplicationDescriptor {
            ClientId = clientId,
            ClientType = ClientTypes.Public,
            ConsentType = ConsentTypes.Explicit,
            DisplayName = displayName,
            Permissions = {
                Permissions.Endpoints.Authorization,
                Permissions.Endpoints.Token,
                Permissions.Endpoints.Revocation,
                Permissions.GrantTypes.AuthorizationCode,
                Permissions.GrantTypes.RefreshToken,
                Permissions.ResponseTypes.Code,
                Permissions.Prefixes.Scope + OAuthConstants.McpScope,
            },
            Requirements = { Requirements.Features.ProofKeyForCodeExchange },
        };
        foreach (var uri in redirectUris)
            descriptor.RedirectUris.Add(new Uri(uri));
        descriptor.Properties[OAuthConstants.Properties.RegisteredVia]
            = JsonSerializer.SerializeToElement(registeredVia);
        descriptor.Properties[OAuthConstants.Properties.RegisteredAt]
            = JsonSerializer.SerializeToElement(DateTime.UtcNow);
        return descriptor;
    }
}
