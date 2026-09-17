using ActualChat.Chat.Db;
using Microsoft.AspNetCore.DataProtection;

namespace ActualChat.Chat;

/// <summary>
/// Protects and unprotects the at-rest secrets of <see cref="DbWebHook"/>:
/// the signing secret (current and rotated-out) and the custom header value.
/// </summary>
public sealed class WebHookSecrets(IServiceProvider services)
{
    private IDataProtector DataProtector { get; }
        = services.GetRequiredService<IDataProtectionProvider>().CreateProtector("WebHooks");

    public string Protect(string value)
        => DataProtector.Protect(value);

    public string Unprotect(string protectedValue)
        => DataProtector.Unprotect(protectedValue);

    public string[] GetSigningSecrets(DbWebHook dbWebHook, Moment now)
    {
        // Newest first; the previous secret is kept only while its overlap window is open
        if (dbWebHook.SecretProtected is not { } secretProtected)
            return [];

        var secret = Unprotect(secretProtected);
        var isPrevActive = dbWebHook.PrevSecretProtected is not null
            && dbWebHook.PrevSecretExpiresAt is { } prevExpiresAt
            && prevExpiresAt.ToMoment() > now;
        return isPrevActive
            ? [secret, Unprotect(dbWebHook.PrevSecretProtected!)]
            : [secret];
    }

    public string? GetCustomHeaderValue(DbWebHook dbWebHook)
        => dbWebHook.CustomHeaderValueProtected is { } headerValueProtected
            ? Unprotect(headerValueProtected)
            : null;
}
