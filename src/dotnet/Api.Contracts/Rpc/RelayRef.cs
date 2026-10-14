using ActualLab.Rpc;

namespace ActualChat.Rpc;

/// <summary>
/// Names a peer that checks one host (the origin or a relay) and nothing else. Every check gets
/// its own Attempt, because a finished peer stays in the hub for a while and its ref would find it.
/// </summary>
[DataContract, MessagePackObject]
public sealed partial record RelayRef(
    [property: DataMember, Key(0)] string Host,
    [property: DataMember, Key(1)] int Attempt)
{
    private const string HostInfoPrefix = "relay|";

    public static bool TryParse(RpcRef rpcRef, [NotNullWhen(true)] out RelayRef? relayRef)
    {
        relayRef = null;
        var hostInfo = rpcRef.HostInfo;
        if (!hostInfo.StartsWith(HostInfoPrefix))
            return false;

        var end = hostInfo.LastIndexOf('|');
        if (end <= HostInfoPrefix.Length || !int.TryParse(hostInfo.AsSpan(end + 1), out var attempt))
            return false;

        relayRef = new RelayRef(hostInfo[HostInfoPrefix.Length..end], attempt);
        return true;
    }

    public static bool IsRelayRef(RpcRef rpcRef)
        => TryParse(rpcRef, out _);

    public RpcRef ToRpcRef()
        => RpcRef.NewClient($"{HostInfoPrefix}{Host}|{Attempt}");
}
