using System.Security.Cryptography;
using Microsoft.Maui.Storage;

namespace ActualChat.Maui;

public sealed class MauiEncryptionKeys(ISecureStorage secureStorage, IPreferences preferences)
{
    private const string DbEncryptionKeyKey = "db_encryption_key";

    public static MauiEncryptionKeys Default { get; } = new(
#if IOS || MACCATALYST || MACOS
        AppleSharedSecureStorage.PerApp,
#else
        SecureStorage.Default,
#endif
        Preferences.Default);

    private readonly Lock _lock = new();
    private byte[]? _dbEncryptionKey;

    private ISecureStorage Storage { get; } = secureStorage;
    private IPreferences LegacyPreferences { get; } = preferences;

    public Task WhenReady {
        get {
            lock (_lock)
                return field ??= Initialize();
        }
    }
    public byte[] DbEncryptionKey
        => _dbEncryptionKey ?? throw new InvalidOperationException("Encryption keys are not ready.");

    // Private methods

    private async Task Initialize()
    {
        var stored = await Storage.GetAsync(DbEncryptionKeyKey).ConfigureAwait(false);
        byte[] key;
        if (stored is not null)
            key = RequireKey(Convert.FromBase64String(stored));
        else {
            var legacy = LegacyPreferences.Get<string?>(DbEncryptionKeyKey, null);
            key = legacy is null
                ? RandomNumberGenerator.GetBytes(32)
                : RequireKey(Serializers.SystemJson.Read<byte[]>(legacy));
            var encodedKey = Convert.ToBase64String(key);
            await Storage.SetAsync(DbEncryptionKeyKey, encodedKey).ConfigureAwait(false);
            var persistedKey = await Storage.GetAsync(DbEncryptionKeyKey).ConfigureAwait(false);
            if (persistedKey != encodedKey)
                throw new IOException("Could not verify the database encryption key in secure storage.");
        }

        LegacyPreferences.Remove(DbEncryptionKeyKey);
        MauiPreferences.RemoveCached(DbEncryptionKeyKey);
        _dbEncryptionKey = key;
    }

    private static byte[] RequireKey(byte[]? key)
        => key is { Length: 32 }
            ? key
            : throw new InvalidDataException("The database encryption key must contain 32 bytes.");
}
