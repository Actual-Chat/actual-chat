namespace ActualChat;

[StructLayout(LayoutKind.Auto)]
public readonly record struct SearchMatchPart(
    Range<int> Range,
    double Rank)
{
    public override string ToString()
        => $"[{Range.Start}..{Range.End}) -> {Rank:F3}";
    public string ToString(string text)
        => $"{JsonFormatter.Format(text[Range.Start..Range.End])} -> {Rank:F3}";
}
