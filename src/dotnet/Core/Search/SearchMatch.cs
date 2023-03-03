namespace ActualChat.Search;

[StructLayout(LayoutKind.Auto)]
[DataContract]
public readonly record struct SearchMatch(
    [property: DataMember(Order = 0)] string Text,
    [property: DataMember(Order = 1)] double Rank,
    SearchMatchPart[] Parts)
{
    public static SearchMatch Empty { get; } = new("");

    private readonly SearchMatchPart[]? _parts = Parts;

    [DataMember(Order = 2)]
    public SearchMatchPart[] Parts {
        get => _parts ?? Array.Empty<SearchMatchPart>();
        init => _parts = value;
    }

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

    public SearchMatch(string? text)
        : this(text ?? "", 0, Array.Empty<SearchMatchPart>())
    { }

    public override string ToString()
    {
        var text = Text;
        var parts = Parts.Select(p => p.ToString(text)).ToDelimitedString(", ");
        return
            $"{GetType().GetName()}(\"{Text}\", {Rank:F3}, {{ {parts} }})";
    }
}
