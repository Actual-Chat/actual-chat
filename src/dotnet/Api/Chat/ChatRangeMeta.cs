namespace ActualChat.Chat;

[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject(AllowPrivate = true)]
[method: SerializationConstructor]
public sealed partial record ChatRangeMeta(
    [property: DataMember, MemoryPackOrder(0), Key(0)] Range<long> IdRange,
    [property: DataMember, MemoryPackOrder(1), Key(1)] Range<long>[] EntryIdRanges,
    [property: DataMember, MemoryPackOrder(2), Key(2)] Range<long>[] ConversationIdRanges,
    [property: DataMember, MemoryPackOrder(3), Key(3)] int MinCount,
    [property: DataMember, MemoryPackIgnore, Key(4)] long? PreviousIdTileStart,
    [property: DataMember, MemoryPackIgnore, Key(5)] long? NextIdTileStart)
{
    [MemoryPackConstructor]
    public ChatRangeMeta() : this(default, default!, default!, default, default, default) { }

    #region MemoryPackXxx properties

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackInclude, MemoryPackOrder(4), IgnoreMember]
    private ApiNullable8<long> MemoryPackPreviousIdTileStart {
        get => PreviousIdTileStart;
        init => PreviousIdTileStart = value;
    }

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackInclude, MemoryPackOrder(5), IgnoreMember]
    private ApiNullable8<long> MemoryPackNextIdTileStart {
        get => NextIdTileStart;
        init => NextIdTileStart = value;
    }

    #endregion
}
