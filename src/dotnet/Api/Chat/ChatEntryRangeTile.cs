namespace ActualChat.Chat;

[DataContract, MessagePackObject]
[method: SerializationConstructor]
public sealed partial record ChatEntryRangeTile(
    [property: DataMember, Key(0)] ChatId? ChatId,
    [property: DataMember, Key(1)] Range<long>[] EntryLidRange,
    [property: DataMember, Key(2)] long? PreviousEntryLid,
    [property: DataMember, Key(3)] long? NextEntryLid)
{
    public static readonly ChatEntryRangeTile None = new(null, [], null, null);

    public ChatEntryRangeTile() : this(default, default!, default, default) { }

}
