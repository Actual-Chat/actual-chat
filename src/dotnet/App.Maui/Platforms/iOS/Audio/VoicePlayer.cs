using ActualChat.UI.Blazor.App.Services;
using ActualLab.Diagnostics;
using ActualLab.Pooling;
using AVFoundation;

namespace ActualChat.App.Maui.Audio;

public class VoicePlayer : IDisposable
{
    private readonly AudioBufferCapacity _capacity;
    private readonly ComputedState<State> _state;
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
        Node = engineLease.Resource.NewPlayer(AudioEngine.VoicePlaybackFormat);
        _capacity = new AudioBufferCapacity(hub);
        Log = hub.LogFor<VoicePlayer>();

        _state = hub.StateFactory.NewComputed(GetState, StateCategories.Get(GetType(), nameof(PlaybackState)));
    }

    public static async Task<VoicePlayer> Create(string id, AppUIHub hub)
    {
        var engineLease = await hub.Services.GetRequiredService<AudioEngines>()
            .Rent(AudioMode.Playback)
            .ConfigureAwait(false);
        return new VoicePlayer(id, engineLease, hub);
    }

    public void Dispose()
    {
        _state.DisposeSilently();
        Node.DisposeSilently();
        EngineLease.DisposeSilently();
    }

    public void Play()
    {
        DebugLog?.LogInformation("#{Id}.Play", Id);
        EngineLease.Resource.EnsureRunning();
        Node.Play();
        _state.Invalidate();
    }

    public void Pause()
    {
        DebugLog?.LogInformation("#{Id}.Pause", Id);
        Node.Pause();
        _state.Invalidate();
    }

    public async ValueTask Feed(AVAudioPcmBuffer pcm, CancellationToken cancellationToken)
    {
        await _capacity.Acquire(cancellationToken).ConfigureAwait(false);
        Node.ScheduleBuffer(pcm,
            _ => {
                // IMPORTANT: better not to access node from the callback thread
                _position += TimeSpan.FromSeconds(pcm.FrameLength / Node.Format.SampleRate);
                _capacity.Release();
                _state.Invalidate();
            });
        _state.Invalidate();
    }

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

    private async Task<State> GetState(CancellationToken cancellationToken)
    {
        var isEngineRunning = await EngineLease.Resource.IsRunning.Computed.Use(cancellationToken).ConfigureAwait(false);
        var isBufferLow = await _capacity.IsBufferLow.Use(cancellationToken).ConfigureAwait(false);
        return new State(_position, Node.IsPlaying && isEngineRunning, isBufferLow);
    }

    public sealed record State(TimeSpan Position, bool IsPlaying, bool IsBufferLow);
}
