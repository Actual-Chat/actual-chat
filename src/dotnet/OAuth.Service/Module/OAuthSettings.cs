namespace ActualChat.OAuth.Module;

public sealed class OAuthSettings
{
    // Empty/null disables the authorization server entirely.
    // Controllers (e.g. OAuthRegistrationController) hardcode "/oauth"; a different value
    // only works for OpenIddict's own endpoints.
    public string Route { get; set; } = "/oauth";
    // Base64 PFX used for both signing and encryption; empty = ephemeral keys (dev/test only).
    public string SigningCertificateBase64 { get; set; } = "";
    public string SigningCertificatePassword { get; set; } = "";
    public TimeSpan AuthorizationCodeLifetime { get; set; } = TimeSpan.FromMinutes(5);
    public TimeSpan AccessTokenLifetime { get; set; } = TimeSpan.FromHours(1);
    public TimeSpan RefreshTokenLifetime { get; set; } = TimeSpan.FromDays(90);
    // Window in which a just-rotated refresh token may be presented again by a client whose refreshes
    // overlapped; outside it a replay revokes the whole grant's tokens.
    public TimeSpan RefreshTokenReuseLeeway { get; set; } = TimeSpan.FromSeconds(30);
    public TimeSpan DcrPruneAge { get; set; } = TimeSpan.FromHours(24);
    public TimeSpan CimdCacheAge { get; set; } = TimeSpan.FromHours(24);
    // Lets a client-metadata document URL (CIMD) be plain http and skips the public-host (anti-SSRF) check on it,
    // so a dev/test server can serve documents from localhost; never affects redirect-URI validation.
    public bool AllowInsecureClientMetadata { get; set; }
}
