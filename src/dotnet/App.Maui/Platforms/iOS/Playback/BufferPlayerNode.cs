using ActualChat.UI.Blazor.App.Services;
using ActualLab.Diagnostics;
using ActualLab.Generators;
using AVFoundation;

namespace ActualChat.App.Maui.Playback;

public class BufferPlayerNode(AVAudioPlayerNode node, AVAudioFormat format, AppUIHub hub)
    : IDisposable
{
    private readonly AudioBufferCapacity _capacity = new ();
    private readonly MutableState<State> _state = hub.StateFactory.NewMutable(new State(TimeSpan.Zero, false, false));
    private TimeSpan _position;

    private string Id { get; } = RandomStringGenerator.Default.Next(5);

    [field: AllowNull, MaybeNull]
    private AudioNodes Nodes => field ??= hub.Services.GetRequiredService<AudioNodes>();
    private ILogger<BufferPlayerNode> Log { get; } = hub.LogFor<BufferPlayerNode>();
    private ILogger? DebugLog => Log.IfEnabled(LogLevel.Debug, Constants.DebugMode.NativeAudioPlayback);

    public IState<State> PlaybackState => _state;

    public void Dispose()
        => Nodes.DisposeNode(node);

    public bool IsPlaying()
        => AudioDispatcher.Invoke(() => node.Playing);

    public void Play()
    {
        DebugLog?.LogInformation("#{Id}.Play", Id);
        Nodes.EnsureNodePlaying(node);
        UpdateState();
    }

    public void Pause()
    {
        DebugLog?.LogInformation("#{Id}.Pause", Id);
        AudioDispatcher.Invoke(node.Pause);
        UpdateState();
    }

    public async ValueTask Feed(AVAudioPcmBuffer pcm, CancellationToken cancellationToken)
    {
        await _capacity.Acquire(cancellationToken).ConfigureAwait(false);
        AudioDispatcher.Invoke(() => {
            node.ScheduleBuffer(pcm,
                AVAudioPlayerNodeCompletionCallbackType.PlayedBack,
                _ => {
                    _position += TimeSpan.FromSeconds(pcm.FrameLength / format.SampleRate);
                    _capacity.Release();
                    _state.Value = new State(_position, true, _capacity.IsBufferLow);
                });
        });
    }

    private void UpdateState()
        => _state.Value = new State(_position, IsPlaying(), _capacity.IsBufferLow);

    public async Task Complete(CancellationToken cancellationToken)
    {
        DebugLog?.LogInformation("#{Id}.End", Id);
        await WhenDonePlaying(cancellationToken).ConfigureAwait(false);
        Stop();
    }

    public void Stop()
    {
        DebugLog?.LogInformation("#{Id}.Stop", Id);
        AudioDispatcher.Invoke(node.Stop);
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
