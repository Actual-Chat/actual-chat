namespace ActualChat.UI.Blazor
{
    public record VirtualListQuery(Range<string> KeyRange)
    {
        public double ExpandStartBy { get; init; }
        public double ExpandEndBy { get; init; }
    }
}
