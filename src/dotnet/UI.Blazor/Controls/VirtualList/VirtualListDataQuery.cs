namespace ActualChat.UI.Blazor.Controls;

public record VirtualListDataQuery(Range<string> InclusiveRange)
{
    public double ExpandStartBy { get; init; }
    public double ExpandEndBy { get; init; }
    public long ExpectedStartExpansion { get; init; }
    public long ExpectedEndExpansion { get; init; }
}
