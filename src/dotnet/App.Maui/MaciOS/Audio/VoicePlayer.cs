using ActualChat.UI.Blazor.App.Services;
using ActualLab.Diagnostics;
using AVFoundation;

namespace ActualChat.App.Maui.Audio;

public class VoicePlayer : IDisposable
{
    private readonly AudioBufferCapacity _capacity;
    private readonly ComputedState<State> _state;
    private TimeSpan _position;

    public string Id { get; }
    private PlayerNode Node { get; }
    private AppUIHub Hub { get; }
    private AudioEngine Engine { get; }
    public IState<State> PlaybackState => _state;
    private ILogger Log => field ??= Hub.LogFor(GetType());
    private ILogger? DebugLog => Log.IfEnabled(LogLevel.Debug, Constants.DebugMode.NativeAudioPlayer);

    public VoicePlayer(string id, AppUIHub hub)
    {
        Id = id;
        Hub = hub;
        var engines = Hub.Services.GetRequiredService<AudioEngines>();
        // On Mac VPIO cancels only the audio rendered through its own unit, so while
        // the mic is capturing, voice playback must go through the Recording engine
        // to become the AEC far-end reference (iOS references the whole device output,
        // so the separate Playback engine is fine there).
        Engine = OperatingSystem.IsMacCatalyst() && engines.Recording.IsRunningNow
            ? engines.Recording
            : engines.Playback;
        Node = Engine.NewPlayer(AudioEngine.VoicePlaybackFormat);

        _capacity = new AudioBufferCapacity(hub);
        _state = Hub.StateFactory.NewComputed(GetState, StateCategories.Get(GetType(), nameof(PlaybackState)));
    }

    public void Dispose()
    {
        _state.DisposeSilently();
        Node.DisposeSilently();
    }

    public void Play()
    {
        DebugLog?.LogInformation("#{Id}.Play", Id);
        Engine.EnsureRunning();
        Node.Play();
    }

    public void Pause()
    {
        DebugLog?.LogInformation("#{Id}.Pause", Id);
        Node.Pause();
    }

    public async ValueTask Feed(AVAudioPcmBuffer pcm, CancellationToken cancellationToken)
    {
        await _capacity.Acquire(cancellationToken).ConfigureAwait(false);
        Node.ScheduleBuffer(pcm,
            _ => {
                // IMPORTANT: better not to access node from the callback thread
                _position += TimeSpan.FromSeconds(pcm.FrameLength / pcm.Format.SampleRate);
                _capacity.Release();
                _state.Invalidate();
            });
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
        var isPlaying = await Node.IsPlaying.Use(cancellationToken).ConfigureAwait(false);
        var isEngineRunning = await Engine.IsRunning.Computed.Use(cancellationToken).ConfigureAwait(false);
        var isBufferLow = await _capacity.IsBufferLow.Use(cancellationToken).ConfigureAwait(false);
        return new State(_position, isPlaying && isEngineRunning, isBufferLow);
    }

    public sealed record State(TimeSpan Position, bool IsPlaying, bool IsBufferLow);
}
