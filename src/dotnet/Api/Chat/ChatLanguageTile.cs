namespace ActualChat.Chat;

[DataContract, MessagePackObject]
public sealed partial class ChatLanguageTile
{
    [DataMember, Key(0)] public Range<long> LidTileRange { get; init; }
    // Entries area always sorted by Id!
    [DataMember, Key(1)] public ChatEntryLanguage[] Entries { get; init; } = [];

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public bool IsEmpty => Entries.Length == 0;

    [JsonConstructor, Newtonsoft.Json.JsonConstructor, SerializationConstructor]
    public ChatLanguageTile() { }

    public ChatLanguageTile(Range<long> lidTileRange, ChatEntryLanguage[] entries)
    {
        LidTileRange = lidTileRange;
        Entries = entries;
    }
}
