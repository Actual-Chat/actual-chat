
namespace ActualChat.Search;

/// <summary>
/// Represents a matched portion of text in a search result.
/// </summary>
[DataContract]
[MessagePackFormatter(typeof(Internal.SearchMatchPartMessagePackFormatter))]
[StructLayout(LayoutKind.Sequential)]
public record struct SearchMatchPart(
    [property: DataMember(Order = 0)] Range<int> Range,
    [property: DataMember(Order = 1)] double Rank)
{
    public override string ToString()
        => $"[{Range.Start}..{Range.End}) -> {Rank:F3}";

    public string ToString(string text)
        => $"\"{text[Range.Start..Range.End]}\" -> {Rank:F3}";
}
