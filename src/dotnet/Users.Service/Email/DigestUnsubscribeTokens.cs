using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;

namespace ActualChat.Users.Email;

/// <summary>
/// Signs the user id that digest emails carry in their one-click unsubscribe link.
/// The token never expires, so the link in an old digest keeps working.
/// </summary>
public sealed class DigestUnsubscribeTokens(IServiceProvider services)
{
    private IDataProtector DataProtector { get; }
        = services.GetRequiredService<IDataProtectionProvider>().CreateProtector("DigestUnsubscribe");

    public string Create(UserId userId)
        => DataProtector.Protect(userId.Value);

    public UserId? TryParse(string token)
    {
        if (token.IsNullOrEmpty())
            return null;

        try {
            var value = DataProtector.Unprotect(token);
            return UserId.TryParse(value, out var userId) ? userId : null;
        }
        catch (CryptographicException) {
            return null;
        }
    }
}
