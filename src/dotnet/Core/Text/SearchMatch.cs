namespace ActualChat;

[StructLayout(LayoutKind.Auto)]
public readonly record struct SearchMatch(
    string Text,
    double Rank,
    SearchMatchPart[] Parts)
{
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

    public SearchMatch(string text)
        : this(text, 0, Array.Empty<SearchMatchPart>())
    { }

    public override string ToString()
    {
        var text = Text;
        var parts = Parts.Select(p => p.ToString(text)).ToDelimitedString(", ");
        return
            $"{GetType().Name}({JsonFormatter.Format(Text)}, {Rank:F3}, {{ {parts} }})";
    }

    // Operators

    public static implicit operator SearchMatch(string text) => new(text);
}
