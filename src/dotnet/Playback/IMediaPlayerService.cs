namespace ActualChat.Playback;

public interface IMediaPlayerService : IAsyncDisposable
{
    [ComputeMethod]
    Task<TrackPlaybackState?> GetPlayingMediaFrame(
        Symbol trackId,
        CancellationToken cancellationToken);

    [ComputeMethod]
    Task<TrackPlaybackState?> GetPlayingMediaFrame(
        Symbol trackId,
        Range<Moment> timestampRange,
        CancellationToken cancellationToken);

    /// <summary>
    ///     Plays the specified media tracks.<br />
    ///     This method shouldn't throw <see cref="OperationCanceledException" /> on cancellation.
    /// </summary>
    /// <param name="commands">Tracks to play.</param>
    /// <param name="cancellationToken">
    ///     Cancellation token.<br />
    ///     This method shouldn't throw <see cref="OperationCanceledException" /> on cancellation.
    /// </param>
    /// <returns>A task that completes once the playback completes or gets cancelled.</returns>
    Task Play(IAsyncEnumerable<MediaPlayerCommand> commands, CancellationToken cancellationToken);
}
