namespace ActualChat.Kvass;

public class KvassForPrefix : IKvass
{
    public string Prefix { get; }
    public IKvass Upstream { get; }

    public KvassForPrefix(string prefix, IKvass upstream)
    {
        Prefix = prefix;
        Upstream = upstream;
    }

    public ValueTask<string?> Get(string key, CancellationToken cancellationToken = default)
        => Upstream.Get(Prefix + key, cancellationToken);

    public ValueTask Set(string key, string? value, CancellationToken cancellationToken = default)
        => Upstream.Set(Prefix + key, value, cancellationToken);
}
