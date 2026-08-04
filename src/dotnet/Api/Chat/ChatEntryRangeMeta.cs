namespace ActualChat.Chat;

[DataContract, MessagePackObject]
[method: SerializationConstructor]
public sealed partial record ChatEntryRangeMeta(
    [property: DataMember, Key(0)] ChatId? ChatId,
    [property: DataMember, Key(1)] Range<long>[] EntryLidRange,
    [property: DataMember, Key(2)] long? PreviousEntryLid,
    [property: DataMember, Key(3)] long? NextEntryLid)
{
    public static readonly ChatEntryRangeMeta None = new(null, [], null, null);

    public ChatEntryRangeMeta() : this(default, default!, default, default) { }

}
