namespace ActualChat.UI.Blazor.App.Services;

/// <summary>
/// Contains an inclusive range of existing chat IDs and offsets for querying chat data.
/// </summary>
/// <param name="ExistingLidRange">Inclusive range!</param>
/// <param name="StartOffset">How many items to load before the ExistingIdRange</param>
/// <param name="EndOffset">How many items to load after the ExistingIdRange</param>
public record ChatDataQuery(Range<long> ExistingLidRange, int StartOffset, int EndOffset)
{
    public ChatViewNavigation? Navigation { get; init; }

    // Currently-visible entry id range. The loaded set must always cover it, so the offsets (which can
    // contract the range — and do so inaccurately next to a very large item) can never drop a visible item.
    public Range<long> VisibleLidRange { get; init; }
    public bool Covers(long entryLid)
        // Offsets are item counts read as lid deltas - the same approximation GetData makes when it
        // decides whether to take a dependency on the chat's id range.
        => entryLid >= ExistingLidRange.Start + StartOffset
            && entryLid <= ExistingLidRange.End + EndOffset;

    public string Format()
#pragma warning disable MA0076
        => $"{ExistingLidRange}@[{StartOffset}-{EndOffset}]";
#pragma warning restore MA0076
}
