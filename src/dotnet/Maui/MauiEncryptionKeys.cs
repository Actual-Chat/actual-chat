using System.Security.Cryptography;
using Microsoft.Maui.Storage;

namespace ActualChat.Maui;

/// <summary>
/// The app's local at-rest encryption keys: the Kvasar stores and the content cache
/// all derive from <see cref="Primary"/>.
/// </summary>
public sealed class MauiEncryptionKeys(ISecureStorage secureStorage, IPreferences preferences)
{
    // Renaming this drops every installed app's key, and with it its LocalSettings
    private const string PrimaryKeyName = "db_encryption_key";

    public static MauiEncryptionKeys Default { get; } = new(
#if IOS || MACCATALYST || MACOS
        AppleSharedSecureStorage.PerApp,
#else
        SecureStorage.Default,
#endif
        Preferences.Default);

    private readonly Lock _lock = new();
    private byte[]? _primary;

    private ISecureStorage Storage { get; } = secureStorage;
    private IPreferences LegacyPreferences { get; } = preferences;

    public Task WhenReady {
        get {
            lock (_lock)
                return field ??= Initialize();
        }
    }
    public byte[] Primary
        => _primary ?? throw new InvalidOperationException("Encryption keys are not ready.");

    // Private methods

    private async Task Initialize()
    {
        var stored = await Storage.GetAsync(PrimaryKeyName).ConfigureAwait(false);
        byte[] key;
        if (stored is not null)
            key = RequireKey(Convert.FromBase64String(stored));
        else {
            var legacy = LegacyPreferences.Get<string?>(PrimaryKeyName, null);
            key = legacy is null
                ? RandomNumberGenerator.GetBytes(32)
                : RequireKey(Serializers.SystemJson.Read<byte[]>(legacy));
            var encodedKey = Convert.ToBase64String(key);
            await Storage.SetAsync(PrimaryKeyName, encodedKey).ConfigureAwait(false);
            var persistedKey = await Storage.GetAsync(PrimaryKeyName).ConfigureAwait(false);
            if (persistedKey != encodedKey)
                throw new IOException("Could not verify the encryption key in secure storage.");
        }

        LegacyPreferences.Remove(PrimaryKeyName);
        MauiPreferences.RemoveCached(PrimaryKeyName);
        _primary = key;
    }

    private static byte[] RequireKey(byte[]? key)
        => key is { Length: 32 }
            ? key
            : throw new InvalidDataException("The encryption key must contain 32 bytes.");
}
