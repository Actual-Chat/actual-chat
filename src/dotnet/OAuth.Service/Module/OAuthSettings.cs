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
    public TimeSpan DcrPruneAge { get; set; } = TimeSpan.FromHours(24);
    public TimeSpan CimdCacheAge { get; set; } = TimeSpan.FromHours(24);
    public bool AllowInsecureClientMetadata { get; set; }
}
