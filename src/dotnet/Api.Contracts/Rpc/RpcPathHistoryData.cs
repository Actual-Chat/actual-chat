namespace ActualChat.Rpc;

[DataContract, MessagePackObject]
public sealed partial record RpcPathHistoryData(
    [property: DataMember, Key(0)] int Version,
    [property: DataMember, Key(1)] Dictionary<string, RpcPathCheck[]> Hosts);
