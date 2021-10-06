namespace ActualChat.UI.Blazor
{
    public record VirtualListQuery(Range<string> IncludedRange)
    {
        public double ExpandStartBy { get; init; }
        public double ExpandEndBy { get; init; }
        public double ExpectedStartExpansion { get; init; }
        public double ExpectedEndExpansion { get; init; }
    }
}
