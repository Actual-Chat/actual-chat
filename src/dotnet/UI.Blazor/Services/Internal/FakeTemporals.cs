namespace ActualChat.UI.Blazor.Services.Internal;

public class FakeTemporals(UIHub hub) : Temporals(hub)
{
    protected sealed override ValueTask<Entry?> GetEntry(string key)
        => default;

    protected sealed override void SetEntry<T>(string key, T value, TimeSpan expiresIn)
    { }

    protected sealed override void RemoveEntry(string key)
    { }
}
