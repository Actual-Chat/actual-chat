using System.Security.Cryptography;
using Microsoft.Maui.Storage;

// ReSharper disable InconsistentlySynchronizedField

namespace ActualChat.Maui;

public static class MauiPreferences
{
    public const string ChatAttentionStateKey = "ChatAttention";

    private const string HostOverrideKey = "app_server_instance_override";
    private const string DbEncryptionKeyKey = "db_encryption_key";
    private const string HostIpKeyPrefix = "host_ip_";
    private const string IsDataCollectionEnabledKey = "analytics";
    private const string ThemeKey = "Theme";
    private const string MinReportableClientVersionKey = "min_reportable_client_version";
    private const string IsPttArmedKey = "is_ptt_armed";

    private static readonly Lock Lock = new();
    private static readonly ConcurrentDictionary<string, object?> Cache = new();
    // A sentinel to distinguish "cached null/not-set" from "not yet cached"
    private static readonly object NotCachedTag = new();

    public static string? HostOverride {
        get => Get<string>(HostOverrideKey).NullIfEmpty();
        set => Set(HostOverrideKey, value ?? "");
    }

    public static byte[] DbEncryptionKey
        => Get(DbEncryptionKeyKey, static () => RandomNumberGenerator.GetBytes(32));

    public static bool? IsDataCollectionEnabled {
        get => Get<bool?>(IsDataCollectionEnabledKey);
        set => Set(IsDataCollectionEnabledKey, value);
    }

    public static string Theme {
        get => Get<string>(ThemeKey) ?? "";
        set => Set(ThemeKey, value);
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
    public static T? Get<T>(string key, Func<T>? factory = null)
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
                var stored = Preferences.Default.Get(key, "");
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
                Set(key, result); // Sets Cache[key] as well
                return result!;
            }

            Cache[key] = result;
            return result;
        }
    }

    public static void Set(string key, object? value)
    {
        lock (Lock) {
            Cache[key] = value;
            switch (value) {
            case null or string { Length: 0 }:
                Preferences.Default.Remove(key);
                return;
            case string str:
                Preferences.Default.Set(key, str);
                return;
            default:
                var stored = Serializers.SystemJson.Write(value, value.GetType());
                Preferences.Default.Set(key, stored);
                return;
            }
        }
    }
}
