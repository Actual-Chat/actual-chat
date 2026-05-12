namespace ActualChat.Rpc;

/// <summary>
/// Snapshot of the current RPC connection's epoch.
/// <see cref="Index"/> is monotonic across the process lifetime and identifies
/// the connection epoch; <see cref="ConnectedAt"/> is when this epoch began.
/// </summary>
public sealed record RpcConnectionInfo(int Index, Moment ConnectedAt);
