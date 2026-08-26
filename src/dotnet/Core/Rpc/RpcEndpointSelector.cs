namespace ActualChat.Rpc;

/// <summary>
/// Picks the host used for RPC connections, which may differ from the app's base
/// host — content URLs keep using the base host.
/// </summary>
public class RpcEndpointSelector
{
    public static RpcEndpointSelector? Instance { get; set; }

    private readonly string[] _candidates;
    private string _current;
    private int _version;

    public IReadOnlyList<string> Candidates => _candidates;
    public string OriginHost => _candidates[0];
    public string Current => Volatile.Read(ref _current);
    public bool IsOnOrigin => string.Equals(Current, OriginHost, StringComparison.OrdinalIgnoreCase);
    public int Version
        // Bumped only when a new network makes every earlier verdict meaningless. Moving
        // between endpoints doesn't bump it, so a fresh move isn't mistaken for a reset.
        => Volatile.Read(ref _version);

    public RpcEndpointSelector(string[] candidates, string? current = null)
    {
        if (candidates.Length == 0)
            throw new ArgumentOutOfRangeException(nameof(candidates));

        _candidates = candidates;
        _current = current != null && candidates.Contains(current, StringComparer.OrdinalIgnoreCase)
            ? current
            : candidates[0];
    }

    public string Get(string host)
        => string.Equals(host, OriginHost, StringComparison.OrdinalIgnoreCase) ? Current : host;

    public void Invalidate()
        => Interlocked.Increment(ref _version);

    public void UseDirect()
    {
        Interlocked.Increment(ref _version);
        Set(OriginHost);
    }

    public bool Use(string endpoint)
    {
        if (!_candidates.Contains(endpoint, StringComparer.OrdinalIgnoreCase))
            return false;

        Set(endpoint);
        return true;
    }

    public bool MoveNext()
    {
        var index = Array.FindIndex(_candidates,
            x => string.Equals(x, Current, StringComparison.OrdinalIgnoreCase));
        var nextIndex = index + 1;
        if (nextIndex >= _candidates.Length)
            return false;

        Set(_candidates[nextIndex]);
        return true;
    }

    public static string ApplyTo(string baseUrl)
    {
        if (Instance is not { } instance)
            return baseUrl;

        var (hostStart, hostEnd) = GetHostRange(baseUrl);
        if (hostStart < 0)
            return baseUrl;

        var host = baseUrl[hostStart..hostEnd];
        var endpoint = instance.Get(host);
        return string.Equals(host, endpoint, StringComparison.OrdinalIgnoreCase)
            ? baseUrl
            : string.Concat(baseUrl.AsSpan(0, hostStart), endpoint, baseUrl.AsSpan(hostEnd));
    }

    public static string WithHost(string baseUrl, string host)
    {
        var (hostStart, hostEnd) = GetHostRange(baseUrl);
        return hostStart < 0
            ? baseUrl
            : string.Concat(baseUrl.AsSpan(0, hostStart), host, baseUrl.AsSpan(hostEnd));
    }

    // Protected/internal methods

    protected virtual void OnChanged(string endpoint) { }

    // Private methods

    private static (int Start, int End) GetHostRange(string baseUrl)
    {
        // Scheme, port, path and query are preserved - only the host moves.
        var schemeEnd = baseUrl.IndexOf("://");
        if (schemeEnd < 0)
            return (-1, -1);

        var hostStart = schemeEnd + 3;
        var hostEnd = baseUrl.IndexOfAny(['/', ':', '?'], hostStart);
        return (hostStart, hostEnd < 0 ? baseUrl.Length : hostEnd);
    }

    private void Set(string endpoint)
    {
        if (string.Equals(Current, endpoint, StringComparison.OrdinalIgnoreCase))
            return;

        // Publication: Get() reads this without a lock.
        Volatile.Write(ref _current, endpoint);
        OnChanged(endpoint);
    }
}
