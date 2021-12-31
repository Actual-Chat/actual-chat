using ActualChat.Media;
using Stl.Concurrency;

namespace ActualChat.MediaPlayback;

public sealed class Playback : AsyncProcessBase, IHasServices
{
    private static long _lastPlaybackIndex;
    private ILogger? _log;

    private ILogger Log => _log ??= Services.LogFor(GetType());
    private ILogger? DebugLog => DebugMode ? Log : null;
    private bool DebugMode => Constants.DebugMode.AudioPlayback;

    private readonly Action _disposeDelegate;

    private IActivePlaybackInfo ActivePlaybackInfo { get; }
    private ITrackPlayerFactory TrackPlayerFactory { get; }
    private DisposeMonitor DisposeMonitor { get; }
    private Channel<PlaybackCommand> Commands { get; set; }

    public IServiceProvider Services { get; }
    public IMutableState<ImmutableList<TrackPlaybackState>> PlayingTracksState { get; }
    public ImmutableList<TrackPlaybackState> PlayingTracks => PlayingTracksState.Value;
    public IMutableState<bool> IsStoppedState { get; }
    public bool IsStopped => IsStoppedState.Value;
    public event Action<TrackPlaybackState>? TrackStarted;
    public event Action<TrackPlaybackState>? TrackStopped;
    public event Action<Playback>? Stopped;

    public Playback(IServiceProvider services, bool start = true)
    {
        Services = services;
        ActivePlaybackInfo = Services.GetRequiredService<IActivePlaybackInfo>();
        TrackPlayerFactory = Services.GetRequiredService<ITrackPlayerFactory>();
        DisposeMonitor = Services.GetRequiredService<DisposeMonitor>();
        Commands = Channel.CreateBounded<PlaybackCommand>(
            new BoundedChannelOptions(128) {
                SingleReader = true,
                SingleWriter = false,
                AllowSynchronousContinuations = true,
                FullMode = BoundedChannelFullMode.DropOldest,
            });
        var stateFactory = Services.StateFactory();
        PlayingTracksState = stateFactory.NewMutable(ImmutableList<TrackPlaybackState>.Empty);
        IsStoppedState = stateFactory.NewMutable(false);
        _disposeDelegate = Dispose;
        DisposeMonitor.Disposed += _disposeDelegate;
        if (start)
            Run();
    }

    public Task Complete()
    {
        Commands.Writer.TryComplete();
        return RunningTask ?? Task.CompletedTask;
    }

    public ValueTask AddCommand(PlaybackCommand command, CancellationToken cancellationToken = default)
        => Commands.Writer.WriteAsync(command, cancellationToken);

    public ValueTask AddMediaTrack(
        Symbol trackId,
        Moment playAt,
        Moment recordingStartedAt,
        IMediaSource source,
        TimeSpan skipTo,
        CancellationToken cancellationToken = default)
    {
        var command = new PlayTrackCommand(trackId,
            playAt,
            recordingStartedAt,
            source,
            skipTo);
        return AddCommand(command, cancellationToken);
    }

    public ValueTask SetVolume(double volume, CancellationToken cancellationToken = default)
        => AddCommand(new SetVolumeCommand(volume), cancellationToken);

    // Protected methods

    protected override async Task RunInternal(CancellationToken cancellationToken)
    {
        var playbackIndex = Interlocked.Increment(ref _lastPlaybackIndex);
        DebugLog?.LogDebug("#{PlaybackIndex} started", playbackIndex);
        var debugStopReason = "n/a";

        var trackPlayers = new ConcurrentDictionary<PlayTrackCommand, TrackPlayer>();
        var trackPlayerStateChanged = TrackPlayerStateChanged; // Just to cache the delegate
        try {
            var commands = Commands.Reader.ReadAllAsync(cancellationToken);
            await foreach (var command in commands.ConfigureAwait(false)) {
                DebugLog?.LogDebug("Playback #{PlaybackIndex}: command {Command}", playbackIndex, command);
                var whenProcessedTaskSource = TaskSource.For(command.WhenProcessed);
                try {
                    switch (command) {
                    case PlayTrackCommand playTrackCommand: {
                        // Reference-based comparison works faster for records.
                        // The { } block here ensures captured commandRef
                        // (a part of closure) won't be modified on every loop iteration.
                        if (trackPlayers.ContainsKey(playTrackCommand))
                            continue;
                        var trackPlayer = TrackPlayerFactory.Create(this, playTrackCommand);
                        trackPlayers[playTrackCommand] = trackPlayer;
                        trackPlayer.StateChanged += trackPlayerStateChanged;
                        _ = trackPlayer.Run(cancellationToken)
                            .ContinueWith(
                                _ => {
                                    if (trackPlayers.TryRemove(playTrackCommand, out var _))
                                        trackPlayer.StateChanged -= trackPlayerStateChanged; // Just in case
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
                        await TryStop(true).ConfigureAwait(false);
                        break;
                    default:
                        throw new NotSupportedException($"Unsupported command type: '{command.GetType()}'.");
                    }
                }
                catch (OperationCanceledException) {
                    if (cancellationToken.IsCancellationRequested)
                        whenProcessedTaskSource.TrySetCanceled(cancellationToken);
                    else
                        // ReSharper disable once MethodSupportsCancellation
                        whenProcessedTaskSource.TrySetCanceled();
                    throw;
                }
                catch (Exception e) {
                    whenProcessedTaskSource.TrySetException(e);
                    throw;
                }
                finally {
                    whenProcessedTaskSource.TrySetResult(default);
                }
            }
            debugStopReason = "end of command sequence";
        }
        catch (OperationCanceledException) {
            debugStopReason = "cancellation";
            _ = TryStop(true);
            throw;
        }
        catch (Exception e) {
            debugStopReason = "error";
            Log.LogError(e, "#{PlaybackIndex} failed", playbackIndex);
            _ = TryStop(true);
            throw;
        }
        finally {
            DisposeMonitor.Disposed -= _disposeDelegate;
            DebugLog?.LogDebug("#{PlaybackIndex} waiting for track players to stop", playbackIndex);
            // ~= await Task.WhenAll(unfinishedPlayTasks).ConfigureAwait(false);
            while (true) {
                var (command, trackPlayer) = trackPlayers.FirstOrDefault();
                if (trackPlayer == null!)
                    break;

                await (trackPlayer.RunningTask ?? Task.CompletedTask).ConfigureAwait(false);
                if (trackPlayers.TryRemove(command, trackPlayer))
                    trackPlayer.StateChanged -= trackPlayerStateChanged; // Just in case
            }
            IsStoppedState.Value = true;
            Stopped?.Invoke(this);
            DebugLog?.LogDebug("#{PlaybackIndex} ended ({StopReason})", playbackIndex, debugStopReason);
        }

        Task TryStop(bool immediately) {
            var stopTasks = trackPlayers.Values
                .Select(p => p.EnqueueCommand(new StopPlaybackCommand(p, immediately)).AsTask())
                .ToArray();
            return Task.WhenAll(stopTasks);
        }
    }

    // Private methods

    private void TrackPlayerStateChanged(
        TrackPlaybackState lastState, TrackPlaybackState state)
    {
        ActivePlaybackInfo.RegisterStateChange(lastState, state);
        if (!lastState.IsStarted && state.IsStarted) {
            lock (Lock) {
                PlayingTracksState.Value = PlayingTracks.Insert(0, state);
            }
            TrackStarted?.Invoke(state);
        }
        else if (state.IsCompleted && !lastState.IsCompleted) {
            lock (Lock) {
                PlayingTracksState.Value = PlayingTracks.RemoveAll(s => s.Command.TrackId == state.Command.TrackId);
            }
            TrackStopped?.Invoke(state);
        }
    }
}
