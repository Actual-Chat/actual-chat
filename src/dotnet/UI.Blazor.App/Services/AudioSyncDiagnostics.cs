namespace ActualChat.UI.Blazor.App.Services;

public enum AudioSyncSkipReason
{
    None = 0,
    SyncDisabled,
    MissingVideoLag,
    MissingAudioLag,
    InsideDeadband,
}

public enum AudioSyncCommand
{
    None = 0,
    SpeedUp,
    Skip,
}

public sealed class AudioSyncDiagnostics : IDisposable
{
    private static readonly TimeSpan CleanupInterval = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan CleanupRetention = TimeSpan.FromMinutes(5);

    private readonly MomentClockSet _clocks;
    private readonly ConcurrentDictionary<AuthorId, Entry> _entries = new();
    private readonly Timer _cleanupTimer;

    public AudioSyncDiagnostics(MomentClockSet clocks)
    {
        _clocks = clocks;
        _cleanupTimer = new Timer(_ => Cleanup(), null, CleanupInterval, CleanupInterval);
    }

    public void RecordDecision(AuthorId authorId, TimeSpan desired, AudioSyncSkipReason reason)
    {
        var now = _clocks.SystemClock.Now;
        _entries.AddOrUpdate(
            authorId,
            _ => new Entry {
                LastDesiredCatchUp = desired,
                LastSkipReason = reason,
                LastDecisionAt = now,
            },
            (_, prev) => prev with {
                LastDesiredCatchUp = desired,
                LastSkipReason = reason,
                LastDecisionAt = now,
            });
    }

    public void RecordCommand(AuthorId authorId, AudioSyncCommand command)
    {
        var now = _clocks.SystemClock.Now;
        _entries.AddOrUpdate(
            authorId,
            _ => new Entry {
                LastCommand = command,
                LastCommandAt = command == AudioSyncCommand.None ? default : now,
                NoOpCount = command == AudioSyncCommand.None ? 1 : 0,
                SpeedUpCount = command == AudioSyncCommand.SpeedUp ? 1 : 0,
                SkipCount = command == AudioSyncCommand.Skip ? 1 : 0,
                LastDecisionAt = now,
            },
            (_, prev) => prev with {
                LastCommand = command,
                LastCommandAt = command == AudioSyncCommand.None ? prev.LastCommandAt : now,
                NoOpCount = prev.NoOpCount + (command == AudioSyncCommand.None ? 1 : 0),
                SpeedUpCount = prev.SpeedUpCount + (command == AudioSyncCommand.SpeedUp ? 1 : 0),
                SkipCount = prev.SkipCount + (command == AudioSyncCommand.Skip ? 1 : 0),
            });
    }

    public AudioSyncDiagnosticsSnapshot? GetSnapshot(AuthorId authorId)
    {
        if (!_entries.TryGetValue(authorId, out var e))
            return null;

        var now = _clocks.SystemClock.Now;
        return new AudioSyncDiagnosticsSnapshot(
            e.LastDesiredCatchUp,
            e.LastSkipReason,
            e.LastCommand,
            e.LastCommandAt == default ? null : now - e.LastCommandAt,
            e.SkipCount,
            e.SpeedUpCount,
            e.NoOpCount);
    }

    public void Dispose() => _cleanupTimer.Dispose();

    private void Cleanup()
    {
        var threshold = _clocks.SystemClock.Now - CleanupRetention;
        foreach (var kv in _entries)
            if (kv.Value.LastDecisionAt < threshold)
                _entries.TryRemove(kv);
    }

    private sealed record Entry
    {
        public TimeSpan LastDesiredCatchUp { get; init; }
        public AudioSyncSkipReason LastSkipReason { get; init; } = AudioSyncSkipReason.None;
        public AudioSyncCommand LastCommand { get; init; } = AudioSyncCommand.None;
        public Moment LastCommandAt { get; init; }
        public Moment LastDecisionAt { get; init; }
        public long SkipCount { get; init; }
        public long SpeedUpCount { get; init; }
        public long NoOpCount { get; init; }
    }
}

[StructLayout(LayoutKind.Auto)]
public readonly record struct AudioSyncDiagnosticsSnapshot(
    TimeSpan LastDesiredCatchUp,
    AudioSyncSkipReason LastSkipReason,
    AudioSyncCommand LastCommand,
    TimeSpan? LastCommandAge,
    long SkipCount,
    long SpeedUpCount,
    long NoOpCount);
