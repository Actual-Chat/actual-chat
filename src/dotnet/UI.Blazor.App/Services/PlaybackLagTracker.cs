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

    public void UpdateAudio(AuthorId authorId, string streamId, TimeSpan lag, LagInputs raw = default)
        => Update(_audio, authorId, streamId, lag, raw);
    public void UpdateVideo(AuthorId authorId, string streamId, TimeSpan lag, LagInputs raw = default)
        => Update(_video, authorId, streamId, lag, raw);

    public TimeSpan? GetAudioLag(AuthorId authorId) => GetMinLag(_audio, authorId);
    public TimeSpan? GetVideoLag(AuthorId authorId) => GetMinLag(_video, authorId);

    public PlaybackLagSnapshot GetSnapshot(AuthorId authorId)
    {
        var now = _clocks.SystemClock.Now;
        var threshold = now - Constants.Audio.PlaybackLagStaleAfter;
        var audio = Scan(_audio, authorId, now, threshold);
        var video = Scan(_video, authorId, now, threshold);
        return new PlaybackLagSnapshot(
            audio.Lag,
            video.Lag,
            audio.FreshCount,
            audio.StaleCount,
            video.FreshCount,
            video.StaleCount,
            audio.FreshestAge,
            video.FreshestAge,
            audio.Raw,
            video.Raw);
    }

    public void Dispose() => _cleanupTimer.Dispose();

    private void Update(ConcurrentDictionary<string, Entry> store,
        AuthorId authorId, string streamId, TimeSpan lag, LagInputs raw)
    {
        var now = _clocks.SystemClock.Now;
        store.AddOrUpdate(streamId,
            _ => new Entry(authorId, lag, now, raw),
            (_, prev) => {
                var staleAfter = Constants.Audio.PlaybackLagStaleAfter;
                var emaLag = prev.AuthorId == authorId && now - prev.UpdatedAt < staleAfter
                    ? prev.Lag * (1 - EmaAlpha) + lag * EmaAlpha
                    : lag;
                return new Entry(authorId, emaLag, now, raw);
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

    private static ScanResult Scan(ConcurrentDictionary<string, Entry> store, AuthorId authorId, Moment now, Moment threshold)
    {
        TimeSpan? min = null;
        var minRaw = default(LagInputs);
        Moment? freshestAt = null;
        var freshCount = 0;
        var staleCount = 0;
        foreach (var kv in store) {
            var e = kv.Value;
            if (e.AuthorId != authorId)
                continue;

            if (freshestAt is null || e.UpdatedAt > freshestAt.Value)
                freshestAt = e.UpdatedAt;
            if (e.UpdatedAt < threshold) {
                staleCount++;
                continue;
            }

            freshCount++;
            if (min is null || e.Lag < min.Value) {
                min = e.Lag;
                minRaw = e.Raw;
            }
        }
        return new ScanResult(min, freshCount, staleCount, freshestAt is null ? null : now - freshestAt.Value, minRaw);
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

    [StructLayout(LayoutKind.Auto)]
    private readonly record struct Entry(
        AuthorId AuthorId,
        TimeSpan Lag,
        Moment UpdatedAt,
        LagInputs Raw);

    [StructLayout(LayoutKind.Auto)]
    private readonly record struct ScanResult(
        TimeSpan? Lag,
        int FreshCount,
        int StaleCount,
        TimeSpan? FreshestAge,
        LagInputs Raw);
}

// Raw lag-formula components (ms) for diagnostics. AnchorMs = source-recorded
// wallclock (server domain); LookaheadMs = forward buffer (audio feeder target
// delay / video buffer span); OffsetMs = presented frame's offset from start.
// SkipRatio (video only) = EMA of present-stage skip activity; the catch-up
// policy suppresses corrections while it's high (audio-master under skip).
// DeviceLatencyMs: audio = AudioContext output latency (baseLatency+outputLatency),
// included in the lag but not catch-up-controllable; video = the pre-present tap
// lag (the lag itself is now the true on-screen rVFC value), kept to show the
// present→display gap. Diagnostics only.
[StructLayout(LayoutKind.Auto)]
public readonly record struct LagInputs(
    double AnchorMs = 0,
    double LookaheadMs = 0,
    double OffsetMs = 0,
    double SkipRatio = 0,
    double DeviceLatencyMs = 0,
    double UplinkMs = 0);

[StructLayout(LayoutKind.Auto)]
public readonly record struct PlaybackLagSnapshot(
    TimeSpan? AudioLag,
    TimeSpan? VideoLag,
    int FreshAudioStreamCount,
    int StaleAudioStreamCount,
    int FreshVideoStreamCount,
    int StaleVideoStreamCount,
    TimeSpan? FreshestAudioAge,
    TimeSpan? FreshestVideoAge,
    LagInputs AudioInputs = default,
    LagInputs VideoInputs = default);
