namespace ActualChat;

[DataContract, MessagePackObject]
public readonly partial record struct MaintenanceKey(
    [property: DataMember(Order = 0), Key(0)] string Value,
    [property: DataMember(Order = 1), Key(1)] ShardKey FullPartitionKey
    ) : IHasShardKey
{
    // Hex digit counts; the full partition key is headed by the partition key, which is headed by the shard key
    public const int ShardKeySize = 1;
    public const int PartitionKeySize = 2;
    public const int FullPartitionKeySize = 4;
    public const int IdPrefixLength = FullPartitionKeySize + 1; // Includes the ':' separator

    public static readonly long ShardCount = ShardKey.KeyCount(ShardKeySize);
    public static readonly long PartitionCount = ShardKey.KeyCount(PartitionKeySize);

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public ShardKey ShardKey => FullPartitionKey.Head(ShardKeySize);
    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public ShardKey PartitionKey => FullPartitionKey.Head(PartitionKeySize);

    public static MaintenanceKey New(string value)
        => new(value, ShardKey.New(value, FullPartitionKeySize));

    public override string ToString()
        => $"{FullPartitionKey}:{Value}";

    public void RequireValid()
    {
        if (Value.IsNullOrEmpty() || Value.Length > 1024)
            throw new ArgumentOutOfRangeException(nameof(Value));
        // Row IDs are sliced at IdPrefixLength, so any other size would misalign them
        if (FullPartitionKey.Size != FullPartitionKeySize)
            throw new ArgumentOutOfRangeException(nameof(FullPartitionKey));
    }
}
