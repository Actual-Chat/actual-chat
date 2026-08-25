using System.Security.Cryptography;
using ActualChat.UI;
using Microsoft.Maui.Storage;

// ReSharper disable InconsistentlySynchronizedField

namespace ActualChat.Maui;

public static class MauiPreferences
{
    public const string ChatAttentionStateKey = "ChatAttention";

    private const string HostOverrideKey = "app_server_instance_override";
    private const string RpcEndpointKey = "rpc_endpoint";
    private const string DbEncryptionKeyKey = "db_encryption_key";
    private const string HostIpKeyPrefix = "host_ip_";
    private const string IsDataCollectionEnabledKey = "analytics";
    private const string ThemeKey = "Theme";
    private const string ThemeColorsKey = "ThemeColors";
    private const string MinReportableClientVersionKey = "min_reportable_client_version";
    private const string IsPttArmedKey = "is_ptt_armed";

    private static readonly Lock Lock = new();
    private static readonly ConcurrentDictionary<string, object?> Cache = new();
    // A sentinel to distinguish "cached null/not-set" from "not yet cached"
    private static readonly object NotCachedTag = new();

#if IOS
    // The App Group container - the only defaults domain the app and its extensions can both
    // see, see App.Maui.IosShareExt/README.md.
    private static string? SharedName => field ??= MauiSettings.IsDevApp
        ? "group.chat.actual.dev.app.shared"
        // ReSharper disable once HeuristicUnreachableCode
        : "group.chat.actual.app.shared";
#else
    // Nowhere else shares these: Mac Catalyst has no extensions and isn't even granted the App
    // Group, and an Android app widget runs in the app's own process. A store name off iOS would
    // only put them in a second file - one the Android backup rules don't know to exclude.
    private static string? SharedName => null;
#endif

    public static string? HostOverride {
        get => Get<string>(HostOverrideKey).NullIfEmpty();
        set => Set(HostOverrideKey, value ?? "");
    }
    public static string? RpcEndpoint {
        get => Get<string>(RpcEndpointKey).NullIfEmpty();
        set => Set(RpcEndpointKey, value ?? "");
    }

    public static byte[] DbEncryptionKey
        => Get(DbEncryptionKeyKey, static () => RandomNumberGenerator.GetBytes(32));

    public static bool? IsDataCollectionEnabled {
        get => Get<bool?>(IsDataCollectionEnabledKey);
        set => Set(IsDataCollectionEnabledKey, value);
    }

    // null means "follow the system appearance"
    public static Theme? Theme {
        get {
            var stored = Get<string>(ThemeKey, sharedName: SharedName);
            return Enum.TryParse<Theme>(stored, false, out var v) ? v : null;
        }
        set => Set(ThemeKey, value?.ToString("G"), SharedName);
    }

    public static string ThemeColors {
        get => Get<string>(ThemeColorsKey, sharedName: SharedName) ?? "";
        set => Set(ThemeColorsKey, value, SharedName);
    }

    public static string? MinReportableClientVersion {
        get => Get<string>(MinReportableClientVersionKey).NullIfEmpty();
        set => Set(MinReportableClientVersionKey, value ?? "");
    }

    public static bool IsPttArmed {
        // Mirrors the app's armed-chat state so MainActivity can raise the PTT foreground
        // service while still in the foreground - the app's own state arrives far too late.
        get => Get<bool?>(IsPttArmedKey) ?? false;
        set => Set(IsPttArmedKey, value);
    }

    public static string? GetHostIp(string hostName)
        => Get<string>(HostIpKeyPrefix + hostName).NullIfEmpty();

    public static void SetHostIp(string hostName, string ip)
        => Set(HostIpKeyPrefix + hostName, ip);

    // Public helpers

    [return: NotNullIfNotNull("factory")]
    public static T? Get<T>(string key, Func<T>? factory = null, string? sharedName = null)
    {
        var cached = Cache.GetValueOrDefault(key, NotCachedTag);
        if (cached != NotCachedTag)
            return cached is T v ? v : default;

        lock (Lock) {
            cached = Cache.GetValueOrDefault(key, NotCachedTag);
            if (cached != NotCachedTag)
                return cached is T v ? v : default;

            T? result = default;
            try {
                var stored = Preferences.Default.Get(key, "", sharedName);
                if (stored.IsNullOrEmpty()) {
                    // No stored value
                }
                else if (typeof(T) == typeof(string))
                    result = (T)(object)stored;
                else
                    result = Serializers.SystemJson.Read<T>(stored);
            }
            catch {
                // Handles type mismatch when stored format changes across versions
            }

            if (result is null && factory != null) {
                result = factory.Invoke();
                Set(key, result, sharedName); // Sets Cache[key] as well
                return result!;
            }

            Cache[key] = result;
            return result;
        }
    }

    public static void Set(string key, object? value, string? sharedName = null)
    {
        lock (Lock) {
            Cache[key] = value;
            switch (value) {
            case null or string { Length: 0 }:
                Preferences.Default.Remove(key, sharedName);
                return;
            case string str:
                Preferences.Default.Set(key, str, sharedName);
                return;
            default:
                var stored = Serializers.SystemJson.Write(value, value.GetType());
                Preferences.Default.Set(key, stored, sharedName);
                return;
            }
        }
    }
}
