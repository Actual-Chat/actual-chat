namespace ActualChat.UI.Blazor
{
    public record VirtualListQuery(Range<string> KeyRange)
    {
        public double MeanItemSize { get; init; }
        public double StartGapSize { get; init; }
        public double EndGapSize { get; init; }
    }
}
