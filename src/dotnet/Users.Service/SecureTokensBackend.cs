using System.Security.Cryptography;
using ActualChat.Security;
using Microsoft.AspNetCore.DataProtection;
using Stl.Generators;

namespace ActualChat.Users;

public class SecureTokensBackend(IServiceProvider services) : ISecureTokensBackend
{
    private static readonly RandomStringGenerator AugmentedPartGenerator = Alphabet.AlphaNumeric64.Generator16;

    private ITimeLimitedDataProtector DataProtector { get; }
        = services.GetRequiredService<IDataProtectionProvider>()
            .CreateProtector(nameof(SecureTokensBackend))
            .ToTimeLimitedDataProtector();
    private IMomentClock Clock { get; } = services.Clocks().SystemClock;

    public ValueTask<SecureToken> Create(string value, CancellationToken cancellationToken = default)
    {
        if (value == null)
            throw new ArgumentNullException(nameof(value));
        if (value.OrdinalContains(' '))
            throw new ArgumentOutOfRangeException(nameof(value), "Value cannot contain space symbols.");

        var augmentedPartLength = Random.Shared.Next(8, 16);
        var augmentedValue = $"{AugmentedPartGenerator.Next(augmentedPartLength)} {value}";
        var expiresAt = Clock.Now + SecureToken.Lifespan;
        var token = SecureToken.Prefix + DataProtector.Protect(augmentedValue, expiresAt.ToDateTimeOffset());
        return ValueTask.FromResult(new SecureToken(token, expiresAt));
    }

    public SecureValue? TryParse(string token)
    {
        if (token.IsNullOrEmpty())
            return null;
        if (!SecureToken.HasValidPrefix(token))
            return null;

        try {
            var augmentedValue = DataProtector.Unprotect(token[SecureToken.Prefix.Length..], out var expiresAt);
            var delimiterIndex = augmentedValue.OrdinalIndexOf(' ');
            if (delimiterIndex < 0 || Clock.UtcNow > expiresAt)
                return null;

            var value = augmentedValue[(delimiterIndex + 1)..];
            return new SecureValue(value, expiresAt);
        }
        catch (CryptographicException) {
            return null;
        }
    }
}
