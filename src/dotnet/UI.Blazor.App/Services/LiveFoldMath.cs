namespace ActualChat.UI.Blazor.App.Services;

public readonly record struct PendingFold(long FoldEndLid, Moment SummaryAt);

public static class LiveFoldMath
{
    public sealed record Result(long BoundaryLid, IReadOnlyList<PendingFold> Pending, Moment? NextWakeAt);

    public static Result Advance(
        long boundaryLid,
        IReadOnlyList<PendingFold> pending,
        Moment serverNow,
        TimeSpan foldLag,
        long? minVisibleLidInBlock)
    {
        var ripeFoldEndLid = 0L;
        Moment? nextWakeAt = null;
        foreach (var fold in pending)
            if (fold.SummaryAt + foldLag <= serverNow)
                ripeFoldEndLid = Math.Max(ripeFoldEndLid, fold.FoldEndLid);
            else {
                var wakeAt = fold.SummaryAt + foldLag;
                if (nextWakeAt == null || wakeAt < nextWakeAt.GetValueOrDefault())
                    nextWakeAt = wakeAt;
            }

        var candidate = ripeFoldEndLid;
        if (minVisibleLidInBlock is { } minVisibleLid)
            candidate = Math.Min(candidate, minVisibleLid);
        var newBoundaryLid = Math.Max(boundaryLid, candidate);
        var remaining = pending.Where(f => f.FoldEndLid > newBoundaryLid).ToList();
        return new Result(newBoundaryLid, remaining, nextWakeAt);
    }
}
