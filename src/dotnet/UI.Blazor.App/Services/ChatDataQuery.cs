namespace ActualChat.UI.Blazor.App.Services;

public record ChatDataQuery(Range<long> IdRange, int LoadBefore = 0, int LoadAfter = 0)
{
    public bool HasVeryFirstItem { get; init; }
    public bool HasVeryLastItem { get; init; }

    public long Start => IdRange.Start - LoadBefore;
    public long End => IdRange.End + LoadAfter;

    public string Format()
        => $"{IdRange.Format()} -{LoadBefore} +{LoadAfter}";
}
