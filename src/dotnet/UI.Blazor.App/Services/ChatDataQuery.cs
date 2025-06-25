namespace ActualChat.UI.Blazor.App.Services;

/// <summary>
/// Contains an inclusive range of existing chat IDs and offsets for querying chat data.
/// </summary>
/// <param name="ExistingIdRange">Inclusive range!</param>
/// <param name="StartOffset">How many items to load before the ExistingIdRange</param>
/// <param name="EndOffset">How many items to load after the ExistingIdRange</param>
public record ChatDataQuery(Range<long> ExistingIdRange, int StartOffset, int EndOffset)
{
    public long? NavigateToLid { get; init; }

    public string Format()
 #pragma warning disable MA0076
        => $"{ExistingIdRange}@[{StartOffset}-{EndOffset}]";
 #pragma warning restore MA0076
}
