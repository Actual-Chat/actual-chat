using ActualChat.UI.Blazor.App.Services;
using ActualLab.Diagnostics;
using ActualLab.Pooling;
using AVFoundation;

namespace ActualChat.App.Maui.Audio;

public class VoicePlayer : WorkerBase
{
    private readonly AudioBufferCapacity _capacity = new ();
    private readonly MutableState<State> _state;
    private TimeSpan _position;
    public string Id { get; }
    private IResourceLease<AudioEngine> EngineLease { get; }

    private PlayerNode Node { get; }
    public IState<State> PlaybackState => _state;
    private ILogger<VoicePlayer> Log { get; }
    private ILogger? DebugLog => Log.IfEnabled(LogLevel.Debug, Constants.DebugMode.NativeAudioPlayback);

    private VoicePlayer(string id, IResourceLease<AudioEngine> engineLease, AppUIHub hub)
    {
        Id = id;
        EngineLease = engineLease;
        _state = hub.StateFactory.NewMutable(new State(TimeSpan.Zero, false, false));
        Node = engineLease.Resource.NewPlayer(AudioEngine.VoicePlaybackFormat);
        Log = hub.LogFor<VoicePlayer>();
        Run();
    }

    public static async Task<VoicePlayer> Create(string id, AppUIHub hub)
    {
        var engineLease = await hub.Services.GetRequiredService<AudioEngines>()
            .Rent(AudioMode.Playback)
            .ConfigureAwait(false);
        return new VoicePlayer(id, engineLease, hub);
    }

    protected override Task DisposeAsyncCore()
    {
        Node.DisposeSilently();
        EngineLease.DisposeSilently();
        return base.DisposeAsyncCore();
    }

    public void Play()
    {
        DebugLog?.LogInformation("#{Id}.Play", Id);
        EngineLease.Resource.EnsureRunning();
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
        => _state.Value = new State(_position, Node.IsPlaying && EngineLease.Resource.IsRunning.Value, _capacity.IsBufferLow);

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

    private Task MonitorEngine(CancellationToken cancellationToken)
        => EngineLease.Resource.IsRunning.Computed.Changes(cancellationToken)
            .ForEachAsync(_ => UpdateState(), cancellationToken);

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
