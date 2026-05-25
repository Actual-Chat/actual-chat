namespace ActualChat.Chat;

[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject]
[method: SerializationConstructor]
public sealed partial record ChatRangeMeta(
    [property: DataMember, MemoryPackOrder(0), Key(0)] Range<long> LidRange,
    [property: DataMember, MemoryPackOrder(1), Key(1)] Range<long>[] EntryLidRanges,
    [property: DataMember, MemoryPackOrder(2), Key(2)] Range<long>[] ConversationLidRanges,
    [property: DataMember, MemoryPackOrder(3), Key(3)] int MinCount,
    [property: DataMember, MemoryPackIgnore, Key(4)] long? PreviousLidTileStart,
    [property: DataMember, MemoryPackIgnore, Key(5)] long? NextLidTileStart)
{
    [MemoryPackConstructor]
    public ChatRangeMeta() : this(default, default!, default!, default, default, default) { }

    #region MemoryPackXxx properties

    [MemoryPackInclude, MemoryPackOrder(4)]
    private ApiNullable8<long> MemoryPackPreviousLidTileStart {
        get => PreviousLidTileStart;
        init => PreviousLidTileStart = value;
    }

    [MemoryPackInclude, MemoryPackOrder(5)]
    private ApiNullable8<long> MemoryPackNextLidTileStart {
        get => NextLidTileStart;
        init => NextLidTileStart = value;
    }

    #endregion
}
