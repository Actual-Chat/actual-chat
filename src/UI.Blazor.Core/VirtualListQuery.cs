namespace ActualChat.UI.Blazor
{
    public record VirtualListQuery(Range<string> InclusiveKeyRange)
    {
        public double ExpandStartBy { get; init; }
        public double ExpandEndBy { get; init; }
    }
}
