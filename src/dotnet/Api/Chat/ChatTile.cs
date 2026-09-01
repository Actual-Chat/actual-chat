namespace ActualChat.Chat;

[DataContract, MessagePackObject]
public sealed partial class ChatTile
{
    [DataMember, Key(0)] public Range<long> LidTileRange { get; init; }
    [DataMember, Key(1)] public bool IncludesRemoved { get; init; }
    [DataMember, Key(2)] public Range<Moment> BeginsAtRange { get; init; }
    // Entries area always sorted by Id!
    [DataMember, Key(3)] public ChatEntry[] Entries { get; init; } = [];

    [JsonIgnore, Newtonsoft.Json.JsonIgnore, IgnoreDataMember, IgnoreMember]
    public bool IsEmpty => Entries.Length == 0;

    [JsonConstructor, Newtonsoft.Json.JsonConstructor, SerializationConstructor]
    public ChatTile() { }

    public ChatTile(Range<long> lidTileRange, bool includesRemoved, ChatEntry[] entries)
    {
        var beginsAtRange = new Range<Moment>(Moment.MaxValue, Moment.MinValue);
        foreach (var entry in entries)
            beginsAtRange = beginsAtRange.MinMaxWith(entry.BeginsAt);

        LidTileRange = lidTileRange;
        IncludesRemoved = includesRemoved;
        BeginsAtRange = (beginsAtRange.Start, beginsAtRange.End + TimeSpan.FromTicks(1));
        Entries = entries;
    }
}
