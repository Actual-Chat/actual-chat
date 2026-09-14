using Microsoft.Maui.Storage;
using Security;

namespace ActualChat.Maui;

// UseDataProtectionKeychain matters only on macOS (AppKit): without it SecItem goes to the
// file-based login keychain, whose per-app ACLs make macOS prompt for the keychain password
// whenever a differently signed build (local vs. App Store) reads the item. iOS and Mac
// Catalyst always use the data protection keychain, so the flag is a no-op there.
public sealed class AppleSharedSecureStorage(string serviceName = "Voxt") : ISecureStorage
{
    private static string AccessGroup => field ??= MauiSettings.IsDevApp
        ? "M287G8G83F.chat.actual.dev.app.shared"
        // ReSharper disable once HeuristicUnreachableCode
        : "M287G8G83F.chat.actual.app.shared";

    public static AppleSharedSecureStorage Default { get; } = new();
    public static AppleSharedSecureStorage PerApp { get; } = new(
        NSBundle.MainBundle.BundleIdentifier + ".encryption");

    private string ServiceName { get; } = serviceName;

    public Task<string?> GetAsync(string key)
    {
        using var query = ExistingQuery(key);
        using var result = SecKeyChain.QueryAsRecord(query, out var resultCode);

        if (resultCode == SecStatusCode.ItemNotFound)
            return Task.FromResult<string?>(null);
        if (resultCode != SecStatusCode.Success || result?.ValueData == null)
            throw new InvalidOperationException($"Failed to read from keychain. Status: {resultCode}");

        var value = NSString.FromData(result.ValueData, NSStringEncoding.UTF8);
        return Task.FromResult<string?>(value?.ToString()
            ?? throw new InvalidDataException("The keychain value is not valid UTF-8."));
    }

    public Task SetAsync(string key, string value)
    {
        using var existingQuery = ExistingQuery(key);
        SecKeyChain.Remove(existingQuery);

        using var newRecord = BuildRecord(key, value);
        var addResult = SecKeyChain.Add(newRecord);

        if (addResult == SecStatusCode.Success)
            return Task.CompletedTask;

        throw new Exception($"Failed to save to keychain. Status: {addResult}");
    }

    public bool Remove(string key)
    {
        using var query = ExistingQuery(key);
        var result = SecKeyChain.Remove(query);
        return result == SecStatusCode.Success || result == SecStatusCode.ItemNotFound;
    }

    public void RemoveAll()
    {
        using var query = ExistingQuery(null);
        SecKeyChain.Remove(query);
    }

    // Private methods

    private SecRecord ExistingQuery(string? key)
    {
        var query = new SecRecord(SecKind.GenericPassword) {
            Account = key,
            Service = ServiceName,
            AccessGroup = AccessGroup,
            UseDataProtectionKeychain = true,
        };
        return query;
    }

    private SecRecord BuildRecord(string key, string value)
        => new(SecKind.GenericPassword) {
            Account = key,
            Label = key,
            Service = ServiceName,
            AccessGroup = AccessGroup,
            UseDataProtectionKeychain = true,
            Accessible = SecAccessible.AfterFirstUnlock,
            ValueData = NSData.FromString(value, NSStringEncoding.UTF8),
        };
}
