namespace ActualChat.Chat;

[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject(true)]
public sealed partial record ChatRangeMeta(
    [property: DataMember, MemoryPackOrder(0)] Range<long> IdRange,
    [property: DataMember, MemoryPackOrder(1)] Range<long>[] EntryIdRanges,
    [property: DataMember, MemoryPackOrder(2)] Range<long>[] ConversationIdRanges,
    [property: DataMember, MemoryPackOrder(3)] int MinCount,
    [property: DataMember, MemoryPackIgnore] long? PreviousIdTileStart,
    [property: DataMember, MemoryPackIgnore] long? NextIdTileStart)
{
    [MemoryPackConstructor, SerializationConstructor]
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
