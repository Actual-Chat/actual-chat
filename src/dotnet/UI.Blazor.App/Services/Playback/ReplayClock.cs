namespace ActualChat.UI.Blazor.App.Services;

/// <summary>
/// A replay's position on its timeline, advanced by the audio that actually plays: a track that
/// starts late or starves holds it, so the tracks after it keep their places relative to it.
/// With nothing playing it runs on wall time. All moments are passed in, on one monotonic scale.
/// </summary>
public sealed class ReplayClock(TimeSpan startedAt, TimeSpan maxExtrapolation)
{
    // Engines report progress every 20-200 ms, so the position is extrapolated between reports -
    // but not by more than this, as a report that doesn't come means the track is starving
    public static readonly TimeSpan DefaultMaxExtrapolation = TimeSpan.FromMilliseconds(300);

    private readonly Lock _lock = new();
    private readonly HashSet<Track> _tracks = new();
    private TimeSpan _position;
    private TimeSpan _idleSince = startedAt;

    public TimeSpan GetPosition(TimeSpan now)
    {
        lock (_lock)
            return GetPositionUnsafe(now);
    }

    public Track StartTrack(TimeSpan playsAt, TimeSpan now)
    {
        lock (_lock) {
            GetPositionUnsafe(now);
            var track = new Track(playsAt);
            _tracks.Add(track);
            return track;
        }
    }

    // played is in timeline time, i.e. the track's own position divided by the replay speed
    public void ReportProgress(Track track, TimeSpan played, bool isPaused, TimeSpan now)
    {
        lock (_lock) {
            if (!_tracks.Contains(track))
                return;

            track.IsPaused = isPaused;
            if (played <= track.Played)
                return;

            track.Played = played;
            track.PlayedAt = now;
        }
    }

    public void EndTrack(Track track, TimeSpan now)
    {
        lock (_lock) {
            GetPositionUnsafe(now);
            if (_tracks.Remove(track) && _tracks.Count == 0)
                _idleSince = now;
        }
    }

    // Private methods

    private TimeSpan GetPositionUnsafe(TimeSpan now)
    {
        // A track that has just started is still behind the position it was started at,
        // so the position holds rather than steps back until that track catches up
        var position = _tracks.Count == 0
            ? _position + (now - _idleSince)
            : _tracks.Min(x => x.GetPosition(now, maxExtrapolation));
        if (_tracks.Count == 0)
            _idleSince = now;
        if (position > _position)
            _position = position;
        return _position;
    }

    // Nested types

    public sealed class Track(TimeSpan playsAt)
    {
        public TimeSpan PlaysAt { get; } = playsAt;
        public TimeSpan Played { get; internal set; }
        public TimeSpan PlayedAt { get; internal set; }
        public bool IsPaused { get; internal set; }

        public TimeSpan GetPosition(TimeSpan now, TimeSpan maxExtrapolation)
        {
            // Nothing is extrapolated before the first audio: a track still buffering holds its start
            if (Played <= TimeSpan.Zero || IsPaused)
                return PlaysAt + Played;

            return PlaysAt + Played + TimeSpanExt.Min(now - PlayedAt, maxExtrapolation);
        }
    }
}
