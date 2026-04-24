namespace ActualChat.Search;

/// <summary>
/// Represents a matched portion of text in a search result.
/// </summary>
[DataContract, MemoryPackable(GenerateType.VersionTolerant), MessagePackObject]
[MessagePackFormatter(typeof(Internal.SearchMatchPartMessagePackFormatter))]
public partial record SearchMatchPart(
    [property: DataMember(Order = 0), MemoryPackOrder(0), Key(0)] Range<int> Range,
    [property: DataMember(Order = 1), MemoryPackOrder(1), Key(1)] double Rank)
{
    public override string ToString()
        => $"[{Range.Start}..{Range.End}) -> {Rank:F3}";
    public string ToString(string text)
        => $"\"{text[Range.Start..Range.End]}\" -> {Rank:F3}";
}
