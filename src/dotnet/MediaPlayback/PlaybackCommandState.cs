namespace ActualChat.MediaPlayback;

public sealed class PlaybackCommandState
{
    public static PlaybackCommandState PlayNothing { get; } = new();

    private readonly TaskSource<Unit> _whenStartedTaskSource;
    private readonly TaskSource<Unit> _whenCompletedTaskSource;

    public IPlaybackCommand Command { get; }

    /// <summary>
    /// Represents a task which will be completed when enqueued command is started. <br/>
    /// For example for <seealso cref="PlayTrackCommand"/> when playing of a track is started.
    /// </summary>
    public Task WhenStarted => _whenStartedTaskSource.Task;

    /// <summary>
    /// Represents a task which will be completed when enqueued command is completed. <br/>
    /// For example for <seealso cref="PlayTrackCommand"/> when actual playing of a track is ended.
    /// </summary>
    public Task WhenCompleted => _whenCompletedTaskSource.Task;

    public PlaybackCommandState(IPlaybackCommand command)
    {
        Command = command;
        // TODO: use <see cref="TaskSource{T}"/> (?)
        _whenStartedTaskSource = TaskSource.New<Unit>(true);
        _whenCompletedTaskSource = TaskSource.New<Unit>(true);
    }

    private PlaybackCommandState() : this(new PlayNothingCommand())
    {
        _whenStartedTaskSource.SetResult(default);
        _whenCompletedTaskSource.SetResult(default);
    }

    public void MarkStarted(Exception? error = null)
    {
        if (error == null)
            _whenStartedTaskSource.SetResult(default);
        else
            _whenStartedTaskSource.SetException(error);
    }

    public void MarkCompleted(Exception? error = null)
    {
        MarkStarted(error);
        if (error == null)
            _whenCompletedTaskSource.SetResult(default);
        else
            _whenCompletedTaskSource.SetException(error);
    }
}
