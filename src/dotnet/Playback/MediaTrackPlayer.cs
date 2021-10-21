using ActualChat.Media;
using Stl.Locking;

namespace ActualChat.Playback;

public abstract class MediaTrackPlayer : AsyncProcessBase
{
    protected ILogger<MediaTrackPlayer> Log { get; init; }
    protected Queue<MediaTrackPlayerCommand> CommandQueue { get; } = new();
    protected AsyncLock CommandQueueLock { get; } = new(ReentryMode.CheckedFail);

    public PlayMediaTrackCommand Command { get; }
    public MediaFrame? PreviousFrame { get; protected set; }
    public MediaFrame? CurrentFrame { get; protected set; }
    public double Volume { get; protected set; } = 1;
    public Task<Unit> WhenStopped { get; private set; }

    public event Func<MediaTrackPlayerCommand, ValueTask>? CommandEnqueued;
    public event Func<MediaTrackPlayerCommand, ValueTask>? CommandProcessed;

    protected MediaTrackPlayer(PlayMediaTrackCommand command, ILogger<MediaTrackPlayer> log)
    {
        Log = log;
        Command = command;
        WhenStopped = TaskSource.New<Unit>(true).Task;
        _ = Run();
    }

    protected override async Task RunInternal(CancellationToken cancellationToken)
    {
        Exception? error = null;
        try {
            await EnqueueCommand(new StartPlaybackCommand(this)).ConfigureAwait(false);
            var frames = Command.Source.Frames;
            await foreach (var frame in frames.WithCancellation(cancellationToken).ConfigureAwait(false))
                await EnqueueCommand(new PlayMediaFrameCommand(this, frame)).ConfigureAwait(false);
        }
        catch (TaskCanceledException) {
            // TODO(AK): this cancellation is requested unexpectedly during regular playback!!!!
            throw;
        }
        catch (Exception ex) when (ex is not OperationCanceledException) {
            error = ex;
            Log.LogError(ex, "Failed to play media track");
        }
        finally {
            var immediately = cancellationToken.IsCancellationRequested || error != null;
            var stopCommand = new StopPlaybackCommand(this, immediately);
            try {
                await EnqueueCommand(stopCommand).ConfigureAwait(false);
            }
            finally {
                try {
                    var isEndedOpt = await WhenStopped
                        .WithTimeout(TimeSpan.FromSeconds(5), CancellationToken.None)
                        .ConfigureAwait(false);
                    if (!isEndedOpt.HasValue) // Let's forcefully push "processed" event for stopCommand
                        await OnCommandProcessed(stopCommand).ConfigureAwait(false);
                }
                finally {
                    // AY: Sorry, but it's a self-disposing thing
                    _ = DisposeAsync();
                }
            }
        }
    }

    public async ValueTask EnqueueCommand(MediaTrackPlayerCommand command)
    {
        using var _ = await CommandQueueLock.Lock().ConfigureAwait(false);
        CommandQueue.Enqueue(command);
        if (CommandEnqueued != null)
            await CommandEnqueued.Invoke(command).ConfigureAwait(false);
        await EnqueueCommandInternal(command).ConfigureAwait(false);
    }

    // Protected methods

    protected abstract ValueTask EnqueueCommandInternal(MediaTrackPlayerCommand command);

    protected async ValueTask OnPlayedTo(TimeSpan offset)
    {
        using var _ = await CommandQueueLock.Lock().ConfigureAwait(false);
        if (WhenStopped.IsCompleted)
            return;
        if (CurrentFrame != null && CurrentFrame.Offset > offset)
            return;
        while (CommandQueue.TryPeek(out var command)) {
            switch (command) {
            case StopPlaybackCommand stopCommand:
            case PlayMediaFrameCommand playCommand when playCommand.Frame.Offset > offset:
                // These commands are definitely further away, so we must exit here
                return;
            }
            // These commands are fine to process & continue
            await OnCommandProcessedInternal(command).ConfigureAwait(false);
            CommandQueue.Dequeue();
        }
    }

    protected async ValueTask OnStopped()
    {
        using var _ = await CommandQueueLock.Lock().ConfigureAwait(false);
        if (WhenStopped.IsCompleted)
            return;
        if (!CommandQueue.Any(c => c is StopPlaybackCommand))
            await EnqueueCommand(new StopPlaybackCommand(this, false)).ConfigureAwait(false);
        while (CommandQueue.TryDequeue(out var command))
            await OnCommandProcessedInternal(command).ConfigureAwait(false);
    }

    protected async ValueTask OnCommandProcessed(MediaTrackPlayerCommand command)
    {
        using var _ = await CommandQueueLock.Lock().ConfigureAwait(false);
        if (WhenStopped.IsCompleted)
            return;
        while (CommandQueue.TryDequeue(out var otherCommand)) {
            await OnCommandProcessedInternal(otherCommand).ConfigureAwait(false);
            if (ReferenceEquals(otherCommand, command))
                return;
        }
        throw new InvalidOperationException("The specified command wasn't ever enqueued.");
    }

    protected virtual async ValueTask OnCommandProcessedInternal(MediaTrackPlayerCommand command)
    {
        if (WhenStopped.IsCompleted)
            return;
        switch (command) {
        case StopPlaybackCommand stop:
            PreviousFrame = CurrentFrame;
            CurrentFrame = null;
            TaskSource.For(WhenStopped).TrySetResult(default!);
            break;
        case PlayMediaFrameCommand playFrame:
            PreviousFrame = CurrentFrame;
            CurrentFrame = playFrame.Frame;
            break;
        case SetTrackVolumeCommand setVolume:
            Volume = setVolume.Volume;
            break;
        }
        if (CommandProcessed != null)
            await CommandProcessed.Invoke(command).ConfigureAwait(false);
    }
}
