using System.Security.Cryptography;
using Microsoft.AspNetCore.DataProtection;
using Stl.Generators;

namespace ActualChat.Users;

public class SecureTokensBackend : ISecureTokensBackend
{
    private static readonly TimeSpan ExpirationPeriod = TimeSpan.FromMinutes(15);
    private static readonly RandomStringGenerator AugmentedPartGenerator = Alphabet.AlphaNumeric64.Generator16;

    private ITimeLimitedDataProtector DataProtector { get; }
    private IMomentClock Clock { get; }

    public SecureTokensBackend(IServiceProvider services)
    {
        DataProtector = services.GetRequiredService<IDataProtectionProvider>()
            .CreateProtector(nameof(SecureTokensBackend))
            .ToTimeLimitedDataProtector();
        Clock = services.Clocks().CoarseSystemClock;
    }

    public ValueTask<SecureToken> Create(string value, CancellationToken cancellationToken)
    {
        if (value == null)
            throw new ArgumentNullException(nameof(value));

        var augmentedPartLength = Random.Shared.Next(8, 16);
        var augmentedValue = $"{AugmentedPartGenerator.Next(augmentedPartLength)} {value}";
        var expiresAt = Clock.Now + ExpirationPeriod;
        var token = DataProtector.Protect(augmentedValue, expiresAt.ToDateTimeOffset());
        return ValueTask.FromResult(new SecureToken(token, expiresAt));
    }

    public ValueTask<Expiring<string>?> TryParse(string token, CancellationToken cancellationToken)
    {
        try {
            var augmentedValue = DataProtector.Unprotect(token, out var expiresAt);
            var delimiterIndex = augmentedValue.IndexOf(' ');
            if (delimiterIndex < 0 || Clock.UtcNow > expiresAt)
                return ValueTask.FromResult<Expiring<string>?>(null);

            var value = augmentedValue[(delimiterIndex + 1)..];
            return ValueTask.FromResult<Expiring<string>?>(new Expiring<string>(value, expiresAt));
        }
        catch (CryptographicException) {
            return ValueTask.FromResult<Expiring<string>?>(null);
        }
    }
}
