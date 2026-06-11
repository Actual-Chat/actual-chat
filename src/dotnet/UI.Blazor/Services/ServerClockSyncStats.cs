namespace ActualChat.UI.Blazor.Services;

public sealed class ServerClockSyncStats
{
    private volatile Snapshot _snapshot = new(TimeSpan.Zero, TimeSpan.FromHours(1), TimeSpan.Zero, TimeSpan.Zero, default, 0);

    public Snapshot Current => _snapshot;

    public void Set(Snapshot snapshot)
        => _snapshot = snapshot;

    public sealed record Snapshot(
        TimeSpan Offset,
        TimeSpan Precision,
        TimeSpan RttEma,
        TimeSpan LastRtt,
        Moment LastSyncAt,
        int SyncCount);
}
