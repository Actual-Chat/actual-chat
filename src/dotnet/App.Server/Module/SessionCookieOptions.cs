namespace ActualChat.App.Server.Module;

public record SessionCookieOptions
{
    public static SessionCookieOptions Default = new ();

    public CookieBuilder Cookie { get; init; } = new() {
        Name = "FusionAuth.SessionId",
        IsEssential = true,
        HttpOnly = true,
        SecurePolicy = CookieSecurePolicy.Always,
        SameSite = SameSiteMode.Lax,
        Expiration = TimeSpan.FromDays(28),
    };
}
