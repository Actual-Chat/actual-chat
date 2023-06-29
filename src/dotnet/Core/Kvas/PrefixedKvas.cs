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

    public ValueTask<byte[]?> Get(string key, CancellationToken cancellationToken = default)
        => Upstream.Get(FullPrefix + key, cancellationToken);

    public Task Set(string key, byte[]? value, CancellationToken cancellationToken = default)
        => Upstream.Set(FullPrefix + key, value, cancellationToken);

    public Task SetMany((string Key, byte[]? Value)[] items, CancellationToken cancellationToken = default)
    {
        var newItems = new (string Key, byte[]? Value)[items.Length];
        for (var i = 0; i < items.Length; i++) {
            var (key, value) = items[i];
            newItems[i] = (FullPrefix + key, value);
        }
        return Upstream.SetMany(newItems, cancellationToken);
    }
}
