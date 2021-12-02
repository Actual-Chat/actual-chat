using System.Collections.Concurrent;
using Stl.Comparison;
using Stl.Concurrency;

namespace ActualChat.Playback;

public abstract class MediaPlayerService : IMediaPlayerService
{
    private readonly ConcurrentDictionary<Symbol, MediaTrackPlaybackState> _trackPlaybackStates = new ();
    private static long _lastPlayIndex;

    protected ILogger<MediaPlayerService> Log { get; }
    private ILogger? DebugLog => DebugMode ? Log : null;
    private bool DebugMode { get; } = Constants.DebugMode.AudioPlayback;

    protected IServiceProvider Services { get; }
    protected CancellationTokenSource StopTokenSource { get; }
    protected CancellationToken StopToken { get; }

    protected MediaPlayerService(
        IServiceProvider services,
        ILogger<MediaPlayerService> log)
    {
        Log = log;
        Services = services;
        StopTokenSource = new();
        StopToken = StopTokenSource.Token;
    }

    public ValueTask DisposeAsync()
    {
        StopTokenSource.CancelAndDisposeSilently();
        return ValueTask.CompletedTask;
    }

    /// <inheritdoc />
    public MediaPlaybackState Play(
        IAsyncEnumerable<MediaPlayerCommand> commands,
        CancellationToken cancellationToken)
    {
        var linkedTokenSource = cancellationToken.LinkWith(StopToken); // Dispose is handled via .ContinueWith later

        var playbackState = new MediaPlaybackState {
            StopToken =  linkedTokenSource.Token
        };
        playbackState.PlayingTask = BackgroundTask.Run(
            () => PlayInternal(playbackState, commands),
            playbackState.StopToken);

        // Disposal
        var _ = playbackState.PlayingTask.ContinueWith(_ => linkedTokenSource.Dispose(), TaskScheduler.Default);
        return playbackState;
    }

    public virtual Task<MediaTrackPlaybackState?> GetMediaTrackPlaybackState(
        Symbol trackId,
        CancellationToken cancellationToken)
        => Task.FromResult(_trackPlaybackStates.GetValueOrDefault(trackId));

    // Protected methods

    protected abstract MediaTrackPlayer CreateMediaTrackPlayer(
        MediaPlaybackState playbackState,
        PlayMediaTrackCommand playTrackCommand);

    protected async Task PlayInternal(MediaPlaybackState playbackState, IAsyncEnumerable<MediaPlayerCommand> commands)
    {
        var cancellationToken = playbackState.StopToken;
        var trackPlayers = playbackState.TrackPlayers;

        var playIndex = Interlocked.Increment(ref _lastPlayIndex);
        DebugLog?.LogDebug("Play #{PlayIndex}: started", playIndex);
        var debugStopReason = "n/a";

        try {
            await foreach (var command in commands.WithCancellation(cancellationToken).ConfigureAwait(false)) {
                DebugLog?.LogDebug("Play #{PlayIndex}: command {Command}", playIndex, command);
                switch (command) {
                case PlayMediaTrackCommand playTrackCommand: {
                    // Reference-based comparison works faster for records.
                    // The { } block here ensures captured commandRef
                    // (a part of closure) won't be modified on every loop iteration.
                    var commandRef = Ref.New(playTrackCommand);
                    if (trackPlayers.ContainsKey(commandRef))
                        continue;
                    var trackPlayer = CreateMediaTrackPlayer(playbackState, playTrackCommand);
                    trackPlayers[commandRef] = trackPlayer;
                    trackPlayer.StateChanged += OnStateChanged;
                    _ = trackPlayer.Run(cancellationToken).ContinueWith(
                        _ => {
                            trackPlayers.TryRemove(commandRef, out var _);
                        },
                        TaskScheduler.Default);
                    break;
                }
                case SetVolumeCommand setVolume:
                    var volumeTasks = trackPlayers.Values
                        .Select(p => p.EnqueueCommand(new SetTrackVolumeCommand(p, setVolume.Volume)).AsTask())
                        .ToArray();
                    await Task.WhenAll(volumeTasks).ConfigureAwait(false);
                    break;
                case StopCommand stopCommand:
                    try {
                        var stopTasks = trackPlayers.Values
                            .Select(p => p.EnqueueCommand(new StopPlaybackCommand(p, true)).AsTask())
                            .ToArray();
                        await Task.WhenAll(stopTasks).ConfigureAwait(false);
                    }
                    finally {
                        stopCommand.CommandProcessedSource.SetResult();
                    }
                    break;
                default:
                    throw new NotSupportedException($"Unsupported command type: '{command.GetType()}'.");
                }
            }
            debugStopReason = "end of command sequence";
        }
        catch (OperationCanceledException) {
            debugStopReason = "cancellation";
            throw;
        }
        catch (Exception e) {
            debugStopReason = "error";
            Log.LogError(e, "Play #{PlayIndex}: failed", playIndex);
            throw;
        }
        finally {
            DebugLog?.LogDebug("Play #{PlayIndex}: waiting for track players to stop", playIndex);
            // ~= await Task.WhenAll(unfinishedPlayTasks).ConfigureAwait(false);
            while (true) {
                var (commandRef, trackPlayer) = trackPlayers.FirstOrDefault();
                if (trackPlayer == null!)
                    break;

                await (trackPlayer.RunningTask ?? Task.CompletedTask).ConfigureAwait(false);
                trackPlayers.TryRemove(commandRef, trackPlayer);
            }
            DebugLog?.LogDebug("Play #{PlayIndex}: ended ({StopReason})", playIndex, debugStopReason);
        }
    }

    protected void OnStateChanged(MediaTrackPlaybackState lastState, MediaTrackPlaybackState state)
    {
        // Debug.WriteLine($"StateChanged: " +
        //     $"({lastState.PlayingAt}, {lastState.IsStarted}, {lastState.IsCompleted}) ->" +
        //     $"({state.PlayingAt}, {state.IsStarted}, {state.IsCompleted})");
        var trackId = state.TrackId;
        if (state.IsCompleted)
            _trackPlaybackStates.TryRemove(trackId, out _);
        else
            _trackPlaybackStates[trackId] = state;

        using (Computed.Invalidate())
            _ = GetMediaTrackPlaybackState(trackId, default);
    }
}
