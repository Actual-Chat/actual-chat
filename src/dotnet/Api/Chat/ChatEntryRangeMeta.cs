namespace ActualChat.Chat;

[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject]
[method: SerializationConstructor]
public sealed partial record ChatEntryRangeMeta(
    [property: DataMember, MemoryPackOrder(0), Key(0)] ChatId? ChatId,
    [property: DataMember, MemoryPackOrder(1), Key(1)] Range<long>[] EntryLidRange,
    [property: DataMember, MemoryPackIgnore, Key(2)] long? PreviousEntryLid,
    [property: DataMember, MemoryPackIgnore, Key(3)] long? NextEntryLid)
{
    public static readonly ChatEntryRangeMeta None = new(null, [], null, null);

    [MemoryPackConstructor]
    public ChatEntryRangeMeta() : this(default, default!, default, default) { }

    #region MemoryPackXxx properties

    [MemoryPackInclude, MemoryPackOrder(2)]
    private ApiNullable8<long> MemoryPackPreviousEntryId {
        get => PreviousEntryLid;
        init => PreviousEntryLid = value;
    }

    [MemoryPackInclude, MemoryPackOrder(3)]
    private ApiNullable8<long> MemoryPackNextEntryId {
        get => NextEntryLid;
        init => NextEntryLid = value;
    }

    #endregion
}
