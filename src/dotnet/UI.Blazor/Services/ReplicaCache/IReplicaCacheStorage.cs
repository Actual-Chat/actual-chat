namespace ActualChat.UI.Blazor.Services;

public interface IReplicaCacheStorage
{
    Task<string?> TryGetValue(string key);
    Task SetValue(string key, string value);
}
