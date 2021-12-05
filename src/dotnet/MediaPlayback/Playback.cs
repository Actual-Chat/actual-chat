using ActualChat.Media;
using Stl.Concurrency;

namespace ActualChat.MediaPlayback;

public sealed class Playback : AsyncProcessBase, IHasServices
{
    private static long _lastPlaybackIndex;

    private ILogger Log { get; }
    private ILogger? DebugLog => DebugMode ? Log : null;
    private bool DebugMode { get; } = Constants.DebugMode.AudioPlayback;

    private readonly Action _disposeDelegate;

    private IActivePlaybackInfo ActivePlaybackInfo { get; }
    private ITrackPlayerFactory TrackPlayerFactory { get; }
    private DisposeMonitor DisposeMonitor { get; }
    private Channel<PlaybackCommand> Commands { get; set; }

    public IServiceProvider Services { get; }
    public bool IsStopped => RunningTask is { IsCompleted: true };
    public ImmutableList<TrackPlaybackState> PlayingTracks { get; private set; } = ImmutableList<TrackPlaybackState>.Empty;
    public IMutableState<ImmutableList<TrackPlaybackState>> PlayingTracksState { get; }
    public event Action<TrackPlaybackState>? TrackStarted;
    public event Action<TrackPlaybackState>? TrackStopped;
    public event Action<Playback>? Stopped;

    public Playback(IServiceProvider services, bool start = true)
    {
        Services = services;
        Log = Services.LogFor(GetType());
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
        PlayingTracksState = Services.StateFactory().NewMutable(Result.New(PlayingTracks));
        _disposeDelegate = Dispose;
        DisposeMonitor.Disposed += _disposeDelegate;
        if (start)
            Run();
    }

    public Task Stop(bool immediately)
    {
        Commands.Writer.TryComplete();
        return immediately ? base.Stop() : RunningTask ?? Task.CompletedTask;
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
            await foreach (var command in commands.WithCancellation(cancellationToken).ConfigureAwait(false)) {
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
                        var stopTasks = trackPlayers.Values
                            .Select(p => p.EnqueueCommand(new StopPlaybackCommand(p, true)).AsTask())
                            .ToArray();
                        await Task.WhenAll(stopTasks).ConfigureAwait(false);
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
            throw;
        }
        catch (Exception e) {
            debugStopReason = "error";
            Log.LogError(e, "#{PlaybackIndex} failed", playbackIndex);
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
            Stopped?.Invoke(this);
            DebugLog?.LogDebug("#{PlaybackIndex} ended ({StopReason})", playbackIndex, debugStopReason);
        }
    }

    // Private methods

    private void TrackPlayerStateChanged(
        TrackPlaybackState lastState, TrackPlaybackState state)
    {
        ActivePlaybackInfo.RegisterStateChange(lastState, state);
        if (!lastState.IsStarted && state.IsStarted) {
            lock (Lock) {
                PlayingTracks = PlayingTracks.Insert(0, state);
            }
            PlayingTracksState.Value = PlayingTracks;
            TrackStarted?.Invoke(state);
        }
        else if (state.IsCompleted && !lastState.IsCompleted) {
            lock (Lock) {
                PlayingTracks = PlayingTracks.RemoveAll(s => s.Command.TrackId == state.Command.TrackId);
            }
            PlayingTracksState.Value = PlayingTracks;
            TrackStopped?.Invoke(state);
        }
    }
}
