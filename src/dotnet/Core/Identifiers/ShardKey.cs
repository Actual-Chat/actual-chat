namespace ActualChat;

// GenerateType.VersionTolerant is not applicable to unmanaged structs (MEMPACK041)

/// <summary>
/// A shard key for RPC methods addressing a specific shard: the call is routed
/// to shard index <c>Value mod ShardCount</c> of the target service's shard scheme.
/// </summary>
[StructLayout(LayoutKind.Auto)]
[DataContract, MemoryPackable, MessagePackObject]
[MessagePackFormatter(typeof(Serialization.Internal.ShardKeyMessagePackFormatter))]
[method: JsonConstructor, Newtonsoft.Json.JsonConstructor, SerializationConstructor]
public readonly partial record struct ShardKey(
    [property: DataMember(Order = 0), MemoryPackOrder(0), Key(0)] int Value)
{
    public static ShardKey New(int value) => new(value);

    public override string ToString() => Value.Format();

    // Conversion

    public static implicit operator int(ShardKey shardKey) => shardKey.Value;
}
