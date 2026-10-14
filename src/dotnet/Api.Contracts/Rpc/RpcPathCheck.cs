namespace ActualChat.Rpc;

[DataContract, MessagePackObject]
public sealed partial record RpcPathCheck(
    [property: DataMember, Key(0)] long AtMs,
    [property: DataMember, Key(1)] bool IsOk,
    [property: DataMember, Key(2)] double PingTimeMs,
    [property: DataMember, Key(3)] double PayloadTimeMs);
