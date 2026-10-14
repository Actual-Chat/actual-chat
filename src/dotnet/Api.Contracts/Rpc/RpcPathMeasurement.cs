namespace ActualChat.Rpc;

// PingTime is a round trip of a tiny RPC call; PayloadTime is a call that returns a larger payload
public sealed record RpcPathMeasurement(TimeSpan PingTime, TimeSpan PayloadTime);
