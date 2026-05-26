using Foundation;

namespace ActualChat.App.Maui.Services;

#if MACCATALYST
/// <summary>
/// Mac Catalyst dev-only ISecureStorage backed by NSUserDefaults. Keychain-backed
/// SecureStorage is sensitive to changes in code signing identity, which means an
/// ad-hoc rebuild (or `dotnet build` after a clean) loses the session — forcing a
/// re-sign-in every iteration. NSUserDefaults is keyed by bundle identifier only,
/// so it survives rebuilds.
///
/// This is NOT used in production builds (see <see cref="MauiSession"/>): values
/// are stored unencrypted in <c>~/Library/Preferences/&lt;bundle&gt;.plist</c>. The
/// session id stored here is not a long-term secret on its own — the server can
/// revoke it — and this only runs on a developer's own machine.
/// </summary>
internal sealed class MacCatalystDevSecureStorage : ISecureStorage
{
    private const string KeyPrefix = "ActualChat.DevSecure.";

    public static readonly MacCatalystDevSecureStorage Default = new();

    public Task<string?> GetAsync(string key)
        => Task.FromResult(NSUserDefaults.StandardUserDefaults.StringForKey(KeyPrefix + key));

    public Task SetAsync(string key, string value)
    {
        NSUserDefaults.StandardUserDefaults.SetString(value, KeyPrefix + key);
        NSUserDefaults.StandardUserDefaults.Synchronize();
        return Task.CompletedTask;
    }

    public bool Remove(string key)
    {
        var fullKey = KeyPrefix + key;
        if (NSUserDefaults.StandardUserDefaults.StringForKey(fullKey) is null)
            return false;
        NSUserDefaults.StandardUserDefaults.RemoveObject(fullKey);
        NSUserDefaults.StandardUserDefaults.Synchronize();
        return true;
    }

    public void RemoveAll()
    {
        var defaults = NSUserDefaults.StandardUserDefaults;
        var dict = defaults.ToDictionary();
        foreach (var k in dict.Keys) {
            var s = k.ToString();
            if (s.StartsWith(KeyPrefix, StringComparison.Ordinal))
                defaults.RemoveObject(s);
        }
        defaults.Synchronize();
    }
}
#endif
