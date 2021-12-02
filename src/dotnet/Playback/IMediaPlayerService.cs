namespace ActualChat.Playback;

public interface IMediaPlayerService : IAsyncDisposable
{
    [ComputeMethod]
    Task<MediaTrackPlaybackState?> GetMediaTrackPlaybackState(
        Symbol trackId,
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
    /// <returns>An object you can use to monitor the playback state.</returns>
    MediaPlaybackState Play(IAsyncEnumerable<MediaPlayerCommand> commands, CancellationToken cancellationToken);
}
