using ActualChat.Rpc;

namespace ActualChat.Maui;

public sealed class MauiRpcEndpointSelector : RpcEndpointSelector
{
    private readonly string _originHost;
    private readonly string[] _candidates;
    private string _current;
    private int _version;

    public static void Use()
        => Instance = new MauiRpcEndpointSelector(MauiSettings.Host, MauiSettings.RpcEndpoints);

    private MauiRpcEndpointSelector(string originHost, string[] candidates)
    {
        _originHost = originHost;
        _candidates = candidates;
        var stored = MauiPreferences.RpcEndpoint;
        _current = stored != null && candidates.Contains(stored, StringComparer.OrdinalIgnoreCase)
            ? stored
            : originHost;
    }

    public override int Version => Volatile.Read(ref _version);

    public override string Get(string host)
        => string.Equals(host, _originHost, StringComparison.OrdinalIgnoreCase)
            ? Volatile.Read(ref _current)
            : host;

    public override void UseDirect()
    {
        Interlocked.Increment(ref _version);
        Set(_originHost);
    }

    public override bool MoveNext()
    {
        Interlocked.Increment(ref _version);
        var current = Volatile.Read(ref _current);
        var index = Array.FindIndex(_candidates, x => string.Equals(x, current, StringComparison.OrdinalIgnoreCase));
        var nextIndex = index + 1;
        if (nextIndex >= _candidates.Length)
            return false;

        Set(_candidates[nextIndex]);
        return true;
    }

    // Private methods

    private void Set(string endpoint)
    {
        if (string.Equals(Volatile.Read(ref _current), endpoint, StringComparison.OrdinalIgnoreCase))
            return;

        // Publication: Get() reads this without a lock.
        Volatile.Write(ref _current, endpoint);
        MauiPreferences.RpcEndpoint = string.Equals(endpoint, _originHost, StringComparison.OrdinalIgnoreCase)
            ? null
            : endpoint;
    }
}
