namespace ActualChat.Kvas;

public class PrefixedKvas : IKvas
{
    public IKvas Upstream { get; }
    public string Prefix { get; }
    public string FullPrefix { get; }

    public PrefixedKvas(IKvas upstream, string prefix)
    {
        Upstream = upstream;
        Prefix = prefix;
        FullPrefix = prefix + ".";
    }

    public ValueTask<string?> Get(string key, CancellationToken cancellationToken = default)
        => Upstream.Get(FullPrefix + key, cancellationToken);

    public Task Set(string key, string? value, CancellationToken cancellationToken = default)
        => Upstream.Set(FullPrefix + key, value, cancellationToken);

    public Task SetMany((string Key, string? Value)[] items, CancellationToken cancellationToken = default)
    {
        var newItems = new (string Key, string? Value)[items.Length];
        for (var i = 0; i < items.Length; i++) {
            var (key, value) = items[i];
            newItems[i] = (FullPrefix + key, value);
        }
        return Upstream.SetMany(newItems, cancellationToken);
    }
}
