using MemoryPack;

namespace ActualChat.Search;

[DataContract, MemoryPackable(GenerateType.VersionTolerant)]
public sealed partial record SearchMatch(
    [property: DataMember(Order = 0), MemoryPackOrder(0)] string Text,
    [property: DataMember(Order = 1), MemoryPackOrder(1)] double Rank,
    SearchMatchPart[] Parts)
{
    public static readonly SearchMatch Empty = New("");

    [DataMember(Order = 2), MemoryPackOrder(2)]
    public SearchMatchPart[] Parts {
        get => field ?? [];
        init;
    } = Parts;

    [MemoryPackIgnore]
    public IEnumerable<SearchMatchPart> PartsWithGaps {
        get {
            var lastIndex = 0;
            foreach (var part in Parts) {
                var range = part.Range;
                if (lastIndex < range.Start)
                    yield return new SearchMatchPart((lastIndex, range.Start), 0);
                yield return part;
                lastIndex = range.End;
            }
            if (lastIndex < Text.Length)
                yield return new SearchMatchPart((lastIndex, Text.Length), 0);
        }
    }

    [IgnoreDataMember, MemoryPackIgnore]
    public bool IsEmpty => this == Empty;

    public static SearchMatch New(string? text)
        => new(text ?? "", 0, []);

    public override string ToString()
    {
        var text = Text;
        var parts = Parts.Select(p => p.ToString(text)).ToDelimitedString(", ");
        return
            $"{GetType().GetName()}(\"{Text}\", {Rank:F3}, {{ {parts} }})";
    }
}
