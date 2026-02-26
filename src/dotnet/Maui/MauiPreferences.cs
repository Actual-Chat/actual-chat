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

    private static readonly Lock Lock = new();
    private static readonly ConcurrentDictionary<string, object?> Cache = new(StringComparer.Ordinal);
    // A sentinel to distinguish "cached null/not-set" from "not yet cached"
    private static readonly object NotCachedTag = new();

    public static string? HostOverride {
        get => Get<string>(HostOverrideKey).NullIfEmpty();
        set => Set(HostOverrideKey, value ?? "");
    }

    public static string DbEncryptionKey
        => Get(DbEncryptionKeyKey, static () => Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)))!;

    public static bool? IsDataCollectionEnabled {
        get => Get<bool?>(IsDataCollectionEnabledKey);
        set => Set(IsDataCollectionEnabledKey, value);
    }

    public static string Theme {
        get => Get<string>(ThemeKey) ?? "";
        set => Set(ThemeKey, value);
    }

    public static string? GetHostIp(string hostName)
        => Get<string>(HostIpKeyPrefix + hostName).NullIfEmpty();

    public static void SetHostIp(string hostName, string ip)
        => Set(HostIpKeyPrefix + hostName, ip);

    // Public helpers

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
                    if (factory != null) {
                        result = factory.Invoke();
                        Set(key, result); // Sets Cache[key] as well
                        return result;
                    }
                    result = typeof(T) == typeof(string)
                        ? (T)(object)stored
                        : default;
                }
                else if (typeof(T) == typeof(string))
                    result = (T)(object)stored;
                else
                    result = SystemJsonSerializer.Default.Read<T>(stored);
            }
            catch {
                // Handles type mismatch when stored format changes across versions
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
                var stored = SystemJsonSerializer.Default.Write(value, value.GetType());
                Preferences.Default.Set(key, stored);
                return;
            }
        }
    }
}
