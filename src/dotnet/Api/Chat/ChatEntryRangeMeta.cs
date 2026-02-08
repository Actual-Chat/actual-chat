
namespace ActualChat.Chat;

[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject(true)]
public sealed partial record ChatEntryRangeMeta(
    [property: DataMember, MemoryPackOrder(0)] ChatId? ChatId,
    [property: DataMember, MemoryPackOrder(1)] Range<long>[] EntryRanges,
    [property: DataMember, MemoryPackIgnore] long? PreviousEntryId,
    [property: DataMember, MemoryPackIgnore] long? NextEntryId)
{
    public static readonly ChatEntryRangeMeta None = new(null, [], null, null);

    [MemoryPackConstructor, SerializationConstructor]
    public ChatEntryRangeMeta() : this(default, default!, default, default) { }

    #region MemoryPackXxx properties

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackInclude, MemoryPackOrder(2), IgnoreMember]
    private ApiNullable8<long> MemoryPackPreviousEntryId {
        get => PreviousEntryId;
        init => PreviousEntryId = value;
    }

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, MemoryPackInclude, MemoryPackOrder(3), IgnoreMember]
    private ApiNullable8<long> MemoryPackNextEntryId {
        get => NextEntryId;
        init => NextEntryId = value;
    }

    #endregion
}
