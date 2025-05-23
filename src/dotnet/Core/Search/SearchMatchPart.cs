using MemoryPack;

namespace ActualChat.Search;

[StructLayout(LayoutKind.Auto)]
[DataContract, MemoryPackable] // Unmanaged struct -> MemoryPackOrder doesn't matter
public readonly partial record struct SearchMatchPart(
    [property: DataMember(Order = 0), MemoryPackOrder(0)] Range<int> Range,
    [property: DataMember(Order = 1), MemoryPackOrder(1)] double Rank)
{
    public override string ToString()
        => $"[{Range.Start}..{Range.End}) -> {Rank:F3}";
    public string ToString(string text)
        => $"\"{text[Range.Start..Range.End]}\" -> {Rank:F3}";
}
