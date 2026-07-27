using System.Security.Cryptography;
using ActualChat.Security;
using Microsoft.AspNetCore.DataProtection;
using ActualLab.Generators;

namespace ActualChat.Users;

public class SecureTokensBackend(IServiceProvider services) : ISecureTokensBackend
{
    private static readonly RandomStringGenerator AugmentedPartGenerator = Alphabet.AlphaNumeric64.Generator16;

    private IDataProtector DataProtector { get; }
        = services.GetRequiredService<IDataProtectionProvider>()
            .CreateProtector(nameof(SecureTokensBackend));
    private MomentClock SystemClock { get; } = services.Clocks().SystemClock;

    public ValueTask<SecureToken> Create(
        SecureTokenKind kind,
        string value,
        CancellationToken cancellationToken = default)
    {
        if (value == null)
            throw new ArgumentNullException(nameof(value));
        if (value.Contains(' '))
            throw new ArgumentOutOfRangeException(nameof(value), "Value cannot contain space symbols.");

        var dataProtector = GetDataProtector(kind);
        var augmentedPartLength = Random.Shared.Next(8, 16);
        var augmentedValue = $"{AugmentedPartGenerator.Next(augmentedPartLength)} {value}";
        var expiresAt = SystemClock.Now + SecureToken.Lifespan + TimeSpan.FromSeconds(15); // Extra time to acquire/refresh
        var token = SecureToken.Prefix + dataProtector.Protect(augmentedValue, expiresAt.ToDateTimeOffset());
        return ValueTask.FromResult(new SecureToken(token, expiresAt));
    }

    public DecryptedSecureToken? TryDecrypt(SecureTokenKind kind, string token)
    {
        if (token.IsNullOrEmpty())
            return null;
        if (!SecureToken.HasValidPrefix(token))
            return null;

        try {
            var dataProtector = GetDataProtector(kind);
            var augmentedValue = dataProtector.Unprotect(token[SecureToken.Prefix.Length..], out var expiresAt);
            var delimiterIndex = augmentedValue.IndexOf(' ');
            if (delimiterIndex < 0 || SystemClock.UtcNow > expiresAt)
                return null;

            var value = augmentedValue[(delimiterIndex + 1)..];
            return new DecryptedSecureToken(value, expiresAt);
        }
        catch (CryptographicException) {
            return null;
        }
    }

    // Private methods

    private ITimeLimitedDataProtector GetDataProtector(SecureTokenKind kind)
        => DataProtector.CreateProtector(kind.ToString()).ToTimeLimitedDataProtector();
}
