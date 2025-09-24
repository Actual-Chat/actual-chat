using ActualChat.UI.Blazor.App.Services;
using ActualLab.Diagnostics;
using ActualLab.Generators;
using AVFoundation;

namespace ActualChat.App.Maui.Playback;

public class BufferPlayerNode : IAsyncDisposable
{
    private readonly AudioBufferCapacity _capacity = new ();
    private readonly MutableState<State> _state;
    private TimeSpan _position;

    private string Id { get; } = RandomStringGenerator.Default.Next(5);
    private AVAudioEngine Engine { get; }
    private AVAudioPlayerNode Node { get; }
    private AVAudioFormat Format { get; }
    private ILogger<BufferPlayerNode> Log { get; }
    private ILogger? DebugLog => Log.IfEnabled(LogLevel.Debug, Constants.DebugMode.NativeAudioPlayback);

    public IState<State> PlaybackState => _state;
    public Task<bool> IsPlaying() => MainThread.InvokeOnMainThreadAsync(() => Node.Playing);

    public BufferPlayerNode(AVAudioEngine engine, AVAudioFormat format, AppUIHub hub)
    {
        Format = format;
        Log = hub.LogFor<BufferPlayerNode>();
        Engine = engine;
        Node = new AVAudioPlayerNode();
        engine.AttachNode(Node);
        engine.Connect(Node, engine.MainMixerNode, format);
        _state = hub.StateFactory.NewMutable(new State(TimeSpan.Zero, false, false));
    }

    public async ValueTask DisposeAsync()
        => await BackgroundTask.Run(() => MainThread.InvokeOnMainThreadAsync(() => {
            Node.Stop();
            Engine.DisconnectNodeInput(Node);
            Engine.DisconnectNodeOutput(Node);
            Engine.DetachNode(Node);
            Node.DisposeSilently();
        })).ConfigureAwait(false);

    public async Task Play()
    {
        DebugLog?.LogInformation("#{Id}.Play", Id);
        if (!Engine.Running) {
            DebugLog?.LogInformation("#{Id}.Play: Engine not running, preparing and starting", Id);
            Engine.AutoShutdownEnabled = false;
            Engine.Prepare();
            Engine.StartAndReturnError(out var nsError);
            nsError.Assert();
        }

        if (!Node.Playing) {
            DebugLog?.LogInformation("#{Id}.Play: Node not playing, starting", Id);
            Node.Volume = 1;
            Node.Play();
        }
        await UpdateState().ConfigureAwait(false);
    }

    public async Task Pause()
    {
        DebugLog?.LogInformation("#{Id}.Pause", Id);
        Node.Pause();
        await UpdateState().ConfigureAwait(false);
    }

    public async ValueTask Feed(AVAudioPcmBuffer pcm, CancellationToken cancellationToken)
    {
        await _capacity.Acquire(cancellationToken).ConfigureAwait(false);
        Node.ScheduleBuffer(pcm,
            AVAudioPlayerNodeCompletionCallbackType.PlayedBack,
            _ => {
                _position += TimeSpan.FromSeconds(pcm.FrameLength / Format.SampleRate);
                _capacity.Release();
                 BackgroundTask.Run(UpdateState, CancellationToken.None);
            });
    }

    private async Task UpdateState()
    {
        var isPlaying = await IsPlaying().ConfigureAwait(false);
        _state.Value = new State(_position, isPlaying, _capacity.IsBufferLow);
    }

    public async Task Complete(CancellationToken cancellationToken)
    {
        DebugLog?.LogInformation("#{Id}.End", Id);
        await WhenDonePlaying(cancellationToken).ConfigureAwait(false);
        await Stop().ConfigureAwait(false);
    }

    public async Task Stop()
    {
        DebugLog?.LogInformation("#{Id}.Stop", Id);
        Log.LogInformation("!!! #{Id}.Stop: stopping, {OperationId}", Id, RandomStringGenerator.Default.Next(3));
        await MainThread.InvokeOnMainThreadAsync(Node.Stop).ConfigureAwait(false);
        Log.LogInformation("!!! #{Id}.Stop: stopped", Id);
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
