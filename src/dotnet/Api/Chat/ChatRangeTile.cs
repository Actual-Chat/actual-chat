namespace ActualChat.Chat;

[DataContract, MessagePackObject]
[method: SerializationConstructor]
public sealed partial record ChatRangeTile(
    [property: DataMember, Key(0)] Range<long> LidRange,
    [property: DataMember, Key(1)] Range<long>[] EntryLidRanges,
    [property: DataMember(Name = "ConversationLidRanges"), Key(2)]
    [property: JsonPropertyName("conversationLidRanges")]
    [property: Newtonsoft.Json.JsonProperty("ConversationLidRanges")]
    Range<long>[] ConversationRanges,
    [property: DataMember, Key(3)] int MinCount,
    [property: DataMember, Key(4)] long? PreviousLidTileStart,
    [property: DataMember, Key(5)] long? NextLidTileStart)
{
    public ChatRangeTile() : this(default, default!, default!, default, default, default) { }

}
