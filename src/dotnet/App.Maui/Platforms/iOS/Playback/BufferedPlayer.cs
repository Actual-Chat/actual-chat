using ActualChat.UI.Blazor.App.Services;
using ActualLab.Diagnostics;
using AVFoundation;

namespace ActualChat.App.Maui.Playback;

public class BufferedPlayer(PlayerNode node, string id, AppUIHub hub)
    : IDisposable
{
    // TODO(FC): remove this complication since there is low buffer management
    private readonly AudioBufferCapacity _capacity = new ();
    private readonly MutableState<State> _state = hub.StateFactory.NewMutable(new State(TimeSpan.Zero, false, false));
    private TimeSpan _position;

    [field: AllowNull, MaybeNull]
    private AudioEngine Engine => field ??= hub.Services.GetRequiredService<AudioEngine>();
    private ILogger<BufferedPlayer> Log { get; } = hub.LogFor<BufferedPlayer>();
    private ILogger? DebugLog => Log.IfEnabled(LogLevel.Debug, Constants.DebugMode.NativeAudioPlayback);

    public IState<State> PlaybackState => _state;

    public void Dispose()
        => node.DisposeSilently();

    public void Play()
    {
        DebugLog?.LogInformation("#{Id}.Play", id);
        Engine.Prepare();
        Engine.EnsureRunning();
        node.Play();
        UpdateState();
    }

    public void Pause()
    {
        DebugLog?.LogInformation("#{Id}.Pause", id);
        node.Pause();
        UpdateState();
    }

    public async ValueTask Feed(AVAudioPcmBuffer pcm, CancellationToken cancellationToken)
    {
        await _capacity.Acquire(cancellationToken).ConfigureAwait(false);
        node.ScheduleBuffer(pcm,
            _ => {
                // IMPORTANT: better not to access node from the callback thread
                _position += TimeSpan.FromSeconds(pcm.FrameLength / node.Format.SampleRate);
                _capacity.Release();
                _state.Value = new State(_position, true, _capacity.IsBufferLow);
            });
    }

    private void UpdateState()
        => _state.Value = new State(_position, node.IsPlaying, _capacity.IsBufferLow);

    public async Task Complete(CancellationToken cancellationToken)
    {
        DebugLog?.LogInformation("#{Id}.End", id);
        await WhenDonePlaying(cancellationToken).ConfigureAwait(false);
        Stop();
    }

    public void Stop()
    {
        DebugLog?.LogInformation("#{Id}.Stop", id);
        node.Stop();
    }

    private async Task WhenDonePlaying(CancellationToken cancellationToken)
    {
        try {
            DebugLog?.LogInformation("#{Id}.WhenDonePlaying: waiting for all queued frames to be played", id);
            await _capacity.AcquireAll(cancellationToken).ConfigureAwait(false);
            DebugLog?.LogInformation("#{Id}.WhenDonePlaying: all frames were played, disconnecting node", id);
        }
        catch (OperationCanceledException e) {
            DebugLog?.LogWarning("#{Id}.WhenDonePlaying: failed to wait for all frames to be played: {Exception}", id, e);
        }
    }

    public sealed record State(TimeSpan Position, bool IsPlaying, bool IsBufferLow);
}
