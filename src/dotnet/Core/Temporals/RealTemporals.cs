namespace ActualChat;

public class RealTemporals : Temporals
{
    public RealTemporals()
        => IsReal = true;

    [ComputeMethod]
    protected override ValueTask<Entry?> GetEntry(string key)
        => new(Entries.GetValueOrDefault(key));

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
