namespace ActualChat.Kvas;

public class KvasForPrefix : IKvas
{
    public string Prefix { get; }
    public IKvas Upstream { get; }

    public KvasForPrefix(string prefix, IKvas upstream)
    {
        Prefix = prefix;
        Upstream = upstream;
    }

    public ValueTask<string?> Get(Symbol key, CancellationToken cancellationToken = default)
        => Upstream.Get(Prefix + key, cancellationToken);

    public void Set(Symbol key, string? value)
        => Upstream.Set(Prefix + key, value);

    public Task Flush(CancellationToken cancellationToken = default)
        => Upstream.Flush(cancellationToken);
}
