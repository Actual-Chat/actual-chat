using ActualChat.UI.Blazor.App.Services;
using ActualLab.Diagnostics;
using AVFoundation;

namespace ActualChat.App.Maui.Audio;

public class BufferedPlayer : WorkerBase
{
    private readonly AudioBufferCapacity _capacity = new ();
    private readonly MutableState<State> _state;
    private TimeSpan _position;
    public string Id { get; }
    private AudioEngine Engine { get; }

    private PlayerNode Node { get; }
    public IState<State> PlaybackState => _state;
    private ILogger<BufferedPlayer> Log { get; }
    private ILogger? DebugLog => Log.IfEnabled(LogLevel.Debug, Constants.DebugMode.NativeAudioPlayback);

    public BufferedPlayer(string id, AudioEngine engine, AppUIHub hub)
    {
        Id = id;
        Engine = engine;
        _state = hub.StateFactory.NewMutable(new State(TimeSpan.Zero, false, false));
        Node = engine.NewPlayer(AudioEngine.VoicePlaybackFormat);
        Log = hub.LogFor<BufferedPlayer>();
        Run();
    }

    protected override Task DisposeAsyncCore()
    {
        Node.DisposeSilently();
        return base.DisposeAsyncCore();
    }

    public void Play()
    {
        DebugLog?.LogInformation("#{Id}.Play", Id);
        Node.Play();
        UpdateState();
    }

    public void Pause()
    {
        DebugLog?.LogInformation("#{Id}.Pause", Id);
        Node.Pause();
        UpdateState();
    }

    public async ValueTask Feed(AVAudioPcmBuffer pcm, CancellationToken cancellationToken)
    {
        await _capacity.Acquire(cancellationToken).ConfigureAwait(false);
        Node.ScheduleBuffer(pcm,
            _ => {
                // IMPORTANT: better not to access node from the callback thread
                _position += TimeSpan.FromSeconds(pcm.FrameLength / Node.Format.SampleRate);
                _capacity.Release();
                _state.Value = new State(_position, true, _capacity.IsBufferLow);
            });
        UpdateState();
    }

    private void UpdateState()
        => _state.Value = new State(_position, Node.IsPlaying && Engine.IsRunning.Value, _capacity.IsBufferLow);

    public async Task Complete(CancellationToken cancellationToken)
    {
        DebugLog?.LogInformation("#{Id}.Complete", Id);
        await WhenDonePlaying(cancellationToken).ConfigureAwait(false);
        Node.Stop();
    }

    public void Abort()
    {
        DebugLog?.LogInformation("#{Id}.Abort", Id);
        Node.Stop();
    }

    protected override Task OnRun(CancellationToken cancellationToken)
    {
        var baseChains = new[] {
            AsyncChain.From(MonitorEngine),
        };
        var retryDelays = RetryDelaySeq.Exp(0.1, 1);
        return (
            from chain in baseChains
            select chain
                .Log(LogLevel.Debug, Log)
                .RetryForever(retryDelays, Log)
            ).RunIsolated(cancellationToken);
    }

    private async Task MonitorEngine(CancellationToken cancellationToken)
    {
        await foreach (var cIsRunning in Engine.IsRunning.Computed.Changes(cancellationToken).ConfigureAwait(false))
            UpdateState();
    }

    private async Task WhenDonePlaying(CancellationToken cancellationToken)
    {
        try {
            DebugLog?.LogInformation("#{Id}.WhenDonePlaying: waiting for all queued frames to be played", Id);
            await _capacity.AcquireAll(cancellationToken).ConfigureAwait(false);
            DebugLog?.LogInformation("#{Id}.WhenDonePlaying: all frames were played, disconnecting node", Id);
        }
        catch (OperationCanceledException e) {
            DebugLog?.LogWarning("#{Id}.WhenDonePlaying: failed to wait for all frames to be played: {Exception}", Id, e);
        }
    }

    public sealed record State(TimeSpan Position, bool IsPlaying, bool IsBufferLow);
}
