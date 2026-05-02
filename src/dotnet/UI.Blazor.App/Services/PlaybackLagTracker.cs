namespace ActualChat.UI.Blazor.App.Services;

/// <summary>
/// Per-stream presentation-lag store used by the audio catch-up policy. The
/// JS audio and video playback paths report lag samples (in ms) every
/// ~500 ms; the tracker maintains an EMA per stream and aggregates the
/// minimum lag per author. Lag-aggregation freshness is gated by the short
/// PlaybackLagStaleAfter window; long-stale entries are pruned periodically
/// to bound memory.
/// </summary>
public sealed class PlaybackLagTracker : IDisposable
{
    private const double EmaAlpha = 0.3;
    // Periodic cleanup cadence — coarse, just to bound memory.
    private static readonly TimeSpan CleanupInterval = TimeSpan.FromMinutes(5);
    // Entries with no update within this window are removed by the cleanup pass.
    // Much longer than PlaybackLagStaleAfter — that one gates per-call
    // freshness for the policy; this one only protects against memory leaks.
    private static readonly TimeSpan CleanupRetention = TimeSpan.FromMinutes(1);

    private readonly MomentClockSet _clocks;
    private readonly ConcurrentDictionary<string, Entry> _audio = new();
    private readonly ConcurrentDictionary<string, Entry> _video = new();
    private readonly Timer _cleanupTimer;

    public PlaybackLagTracker(MomentClockSet clocks)
    {
        _clocks = clocks;
        _cleanupTimer = new Timer(_ => Cleanup(), null, CleanupInterval, CleanupInterval);
    }

    public void UpdateAudio(AuthorId authorId, string streamId, TimeSpan lag)
        => Update(_audio, authorId, streamId, lag);
    public void UpdateVideo(AuthorId authorId, string streamId, TimeSpan lag)
        => Update(_video, authorId, streamId, lag);

    public TimeSpan? GetAudioLag(AuthorId authorId) => GetMinLag(_audio, authorId);
    public TimeSpan? GetVideoLag(AuthorId authorId) => GetMinLag(_video, authorId);

    public void Dispose() => _cleanupTimer.Dispose();

    private void Update(ConcurrentDictionary<string, Entry> store,
        AuthorId authorId, string streamId, TimeSpan lag)
    {
        var now = _clocks.SystemClock.Now;
        store.AddOrUpdate(streamId,
            _ => new Entry(authorId, lag, now),
            (_, prev) => {
                var staleAfter = Constants.Audio.PlaybackLagStaleAfter;
                var emaLag = prev.AuthorId == authorId && now - prev.UpdatedAt < staleAfter
                    ? prev.Lag * (1 - EmaAlpha) + lag * EmaAlpha
                    : lag;
                return new Entry(authorId, emaLag, now);
            });
    }

    private TimeSpan? GetMinLag(ConcurrentDictionary<string, Entry> store, AuthorId authorId)
    {
        var threshold = _clocks.SystemClock.Now - Constants.Audio.PlaybackLagStaleAfter;
        TimeSpan? min = null;
        foreach (var kv in store) {
            var e = kv.Value;
            if (e.AuthorId != authorId || e.UpdatedAt < threshold)
                continue;

            if (min is null || e.Lag < min.Value)
                min = e.Lag;
        }
        return min;
    }

    private void Cleanup()
    {
        var threshold = _clocks.SystemClock.Now - CleanupRetention;
        Sweep(_audio, threshold);
        Sweep(_video, threshold);
    }

    private static void Sweep(ConcurrentDictionary<string, Entry> store, Moment threshold)
    {
        foreach (var kv in store)
            if (kv.Value.UpdatedAt < threshold)
                store.TryRemove(kv);
    }

    private readonly record struct Entry(AuthorId AuthorId, TimeSpan Lag, Moment UpdatedAt);
}
