namespace ActualChat.Kvas;

public class KvasForPrefix : IKvas
{
    public string Prefix { get; }
    public string FullPrefix { get; }
    public IKvas Upstream { get; }

    public KvasForPrefix(string prefix, IKvas upstream)
    {
        Prefix = prefix;
        FullPrefix = prefix + ".";
        Upstream = upstream;
    }

    public ValueTask<string?> Get(Symbol key, CancellationToken cancellationToken = default)
        => Upstream.Get(FullPrefix + key, cancellationToken);

    public Task Set(Symbol key, string? value, CancellationToken cancellationToken = default)
        => Upstream.Set(FullPrefix + key, value, cancellationToken);

    public Task Flush(CancellationToken cancellationToken = default)
        => Upstream.Flush(cancellationToken);
}
