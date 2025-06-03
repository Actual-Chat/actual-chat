namespace ActualChat.UI.Blazor.App.Services;

public record ChatDataQuery(Range<long> ExistingIdRange, int StartOffset, int EndOffset)
{
    public string Format()
 #pragma warning disable MA0076
        => $"{ExistingIdRange}@[{StartOffset}-{EndOffset}]";
 #pragma warning restore MA0076
}
