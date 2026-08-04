namespace ActualChat.Chat;

[DataContract, MessagePackObject]
[method: SerializationConstructor]
public sealed partial record ChatRangeMeta(
    [property: DataMember, Key(0)] Range<long> LidRange,
    [property: DataMember, Key(1)] Range<long>[] EntryLidRanges,
    [property: DataMember, Key(2)] Range<long>[] ConversationLidRanges,
    [property: DataMember, Key(3)] int MinCount,
    [property: DataMember, Key(4)] long? PreviousLidTileStart,
    [property: DataMember, Key(5)] long? NextLidTileStart)
{
    public ChatRangeMeta() : this(default, default!, default!, default, default, default) { }

}
