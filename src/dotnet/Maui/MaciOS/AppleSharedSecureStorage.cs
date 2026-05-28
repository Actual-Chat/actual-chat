using Microsoft.Maui.Storage;
using Security;

namespace ActualChat.Maui;

public class AppleSharedSecureStorage : ISecureStorage
{
    private const string ServiceName = "Voxt";
    private static string AccessGroup => field ??= MauiSettings.IsDevApp
        ? "M287G8G83F.chat.actual.dev.app.shared"
        // ReSharper disable once HeuristicUnreachableCode
        : "M287G8G83F.chat.actual.app.shared";

    public static AppleSharedSecureStorage Default { get; } = new ();

    public Task<string?> GetAsync(string key)
    {
        using var query = ExistingQuery(key);
        using var result = SecKeyChain.QueryAsRecord(query, out var resultCode);

        if (resultCode != SecStatusCode.Success || result?.ValueData == null)
            return Task.FromResult<string?>(null);

        var value = NSString.FromData(result.ValueData, NSStringEncoding.UTF8);
        return Task.FromResult(value?.ToString());
    }

    public Task SetAsync(string key, string value)
    {
        using var existingQuery = ExistingQuery(key);
        SecKeyChain.Remove(existingQuery);

        // Add new item
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

    private SecRecord ExistingQuery(string? key)
    {
        var query = new SecRecord(SecKind.GenericPassword)
        {
            Account = key,
            Service = ServiceName,
            AccessGroup = AccessGroup,
        };
        return query;
    }


    private SecRecord BuildRecord(string key, string value)
        => new (SecKind.GenericPassword)
        {
            Account = key,
            Label = key,
            Service = ServiceName,
            AccessGroup = AccessGroup,
            Accessible = SecAccessible.AfterFirstUnlock,
            ValueData = NSData.FromString(value, NSStringEncoding.UTF8),
        };
}
