namespace ActualChat.UI.Blazor.Services;

public enum ServerClockSyncMode
{
    Converging = 0,
    Steady = 1,
}

public enum ServerClockSyncOutcome
{
    None = 0,
    Refinement = 1,
    Step = 2,
    RejectNoisy = 3,
    RejectTransient = 4,
    BurstDiscarded = 5,
}

public sealed class ServerClockSyncStats
{
    private Snapshot _snapshot =
        new(TimeSpan.Zero, TimeSpan.FromHours(1), TimeSpan.Zero, TimeSpan.Zero, default, default, 0);
    public Snapshot Current
        // Publication: built by the sync loop, read by diagnostics UI
        => Volatile.Read(ref _snapshot);
    public void Set(Snapshot snapshot)
        => Volatile.Write(ref _snapshot, snapshot);

    // Nested types

    public sealed record Snapshot(
        TimeSpan Offset,
        TimeSpan Precision,
        TimeSpan RttEma,
        TimeSpan LastRtt,
        Moment LastSyncAt,
        Moment LastSyncWallAt,
        int SyncCount)
    {
        public TimeSpan Dispersion { get; init; }
        public TimeSpan Correction { get; init; }
        public TimeSpan Staleness { get; init; }
        public int Epoch { get; init; }
        public ServerClockSyncMode Mode { get; init; }
        public ServerClockSyncOutcome Outcome { get; init; }
    }
}
