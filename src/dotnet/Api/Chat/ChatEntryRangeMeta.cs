namespace ActualChat.Chat;

[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject(AllowPrivate = true)]
[method: SerializationConstructor]
public sealed partial record ChatEntryRangeMeta(
    [property: DataMember, MemoryPackOrder(0), Key(0)] ChatId? ChatId,
    [property: DataMember, MemoryPackOrder(1), Key(1)] Range<long>[] EntryRanges,
    [property: DataMember, MemoryPackIgnore, Key(2)] long? PreviousEntryLid,
    [property: DataMember, MemoryPackIgnore, Key(3)] long? NextEntryLid)
{
    public static readonly ChatEntryRangeMeta None = new(null, [], null, null);

    [MemoryPackConstructor]
    public ChatEntryRangeMeta() : this(default, default!, default, default) { }

    #region MemoryPackXxx properties

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackInclude, MemoryPackOrder(2), IgnoreMember]
    private ApiNullable8<long> MemoryPackPreviousEntryId {
        get => PreviousEntryLid;
        init => PreviousEntryLid = value;
    }

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackInclude, MemoryPackOrder(3), IgnoreMember]
    private ApiNullable8<long> MemoryPackNextEntryId {
        get => NextEntryLid;
        init => NextEntryLid = value;
    }

    #endregion
}
