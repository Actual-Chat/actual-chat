namespace ActualChat.UI.Blazor.Services.Internal;

public class RealTemporals(UIHub hub) : Temporals(hub)
{
    [ComputeMethod]
    protected override ValueTask<Entry?> GetEntry(string key)
        => new (Entries.GetValueOrDefault(key));

    protected override void SetEntry<T>(string key, T value, TimeSpan expiresIn)
    {
        var entry = new Entry<T>(this, key, value, expiresIn);
        entry.StartExpiration();
    }

    protected override void RemoveEntry(string key)
    {
        var entry = Entries.GetValueOrDefault(key);
        entry?.EndExpiration();
    }
}
