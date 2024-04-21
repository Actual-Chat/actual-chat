namespace ActualChat.Kvas;

public class PrefixedKvas(IKvas upstream, string prefix) : IKvas
{
    public IKvas Upstream { get; } = upstream;
    public string Prefix { get; } = prefix;
    public string FullPrefix { get; } = prefix + ".";

    public ValueTask<byte[]?> Get(string key, CancellationToken cancellationToken = default)
        => Upstream.Get(FullPrefix + key, cancellationToken);

    public ValueTask<ApiList<(string Key, byte[] Value)>> List(string keyPrefix, CancellationToken cancellationToken = default)
        => Upstream.List(FullPrefix + keyPrefix, cancellationToken);

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
