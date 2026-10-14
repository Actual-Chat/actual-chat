namespace ActualChat.Rpc;

/// <summary>
/// What to do on connect: the hosts in the order to try them, how long to wait before trying the
/// next host next to the first one (null - don't), and which others to check again in the
/// background because their records are missing or stale.
/// </summary>
public sealed record RpcPathPlan(
    IReadOnlyList<string> HostsToTry,
    bool IsFirstHostKnownGood,
    TimeSpan? StartNextAfter,
    IReadOnlyList<string> HostsToRefresh)
{
    public string FirstHost => HostsToTry[0];
}
