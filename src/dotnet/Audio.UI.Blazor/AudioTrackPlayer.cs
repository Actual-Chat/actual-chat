using System.Reflection;
using ActualChat.Audio.UI.Blazor.Components;
using ActualChat.Media;
using ActualChat.Playback;
using Microsoft.JSInterop;
using Stl.Fusion.Blazor;

namespace ActualChat.Audio.UI.Blazor;

public class AudioTrackPlayer : MediaTrackPlayer, IAudioPlayerBackend
{
    private readonly BlazorCircuitContext _circuitContext;
    private readonly byte[] _header;
    private readonly IJSRuntime _js;
    private DotNetObjectReference<IAudioPlayerBackend>? _blazorRef;
    private CancellationTokenSource _delayTokenSource;
    private IJSObjectReference? _jsRef;
    private bool DebugMode { get; } = Constants.DebugMode.AudioTrackPlayer;

    public AudioSource AudioSource => (AudioSource)Source;

    public AudioTrackPlayer(
        PlayMediaTrackCommand command,
        BlazorCircuitContext circuitContext,
        IJSRuntime js,
        MomentClockSet clocks,
        ILogger<AudioTrackPlayer> log)
        : base(command, clocks, log)
    {
        _circuitContext = circuitContext;
        _js = js;
        _delayTokenSource = new CancellationTokenSource();
        _header = AudioSource.Format.ToBlobPart().Data;
    }

    [JSInvokable]
    public async Task OnPlaybackEnded(int? errorCode, string? errorMessage)
    {
        Exception? error = null;
        if (errorMessage != null) {
            error = new TargetInvocationException(
                $"Playback stopped with an error, code = {errorCode}, message = '{errorMessage}'.",
                null);
            Log.LogError(error, "Playback stopped with an error");
        }

        OnStopped(error);

        var jsRef = _jsRef;
        _jsRef = null;

        if (jsRef != null)
            await jsRef.DisposeAsync().ConfigureAwait(true);
    }

    [JSInvokable]
    public Task OnPlaybackTimeChanged(double? offset)
    {
        if (offset != null)
            OnPlayedTo(TimeSpan.FromSeconds(offset.Value));

        return Task.CompletedTask;
    }

    [JSInvokable]
    public Task OnDataWaiting(double? offset, int? readyState)
    {
        _delayTokenSource.Cancel();
        _delayTokenSource.Dispose();
        _delayTokenSource = new CancellationTokenSource();

        Log.LogWarning("Waiting for audio data. Offset = {Offset}, readyState = {ReadyState}", offset, readyState);

        return Task.CompletedTask;
    }

    [JSInvokable]
    public void OnVolumeChanged(double? volume)
    {
        if (volume != null)
            OnVolumeSet(volume.Value);
    }

    protected override async ValueTask ProcessCommand(MediaTrackPlayerCommand command)
        => await CircuitInvoke(async () => {
                switch (command) {
                    case StartPlaybackCommand:
                        _blazorRef = DotNetObjectReference.Create<IAudioPlayerBackend>(this);
                        _jsRef = await _js.InvokeAsync<IJSObjectReference>(
                            $"{AudioBlazorUIModule.ImportName}.AudioPlayer.create",
                            _blazorRef,
                            DebugMode);
                        await _jsRef!.InvokeVoidAsync("initialize", _header);
                        break;
                    case StopPlaybackCommand stop:
                        if (_jsRef == null)
                            break;

                        if (stop.Immediately)
                            await _jsRef.InvokeVoidAsync("stop", null);
                        else
                            await _jsRef.InvokeVoidAsync("endOfStream");
                        break;
                    case SetTrackVolumeCommand setVolume:
                        // TODO: Implement this
                        break;
                    default:
                        throw new NotSupportedException($"Unsupported command type: '{command.GetType()}'.");
                }
            })
            .ConfigureAwait(false);

    protected override async ValueTask ProcessMediaFrame(MediaFrame frame, CancellationToken cancellationToken)
        => await CircuitInvoke(async () => {
                if (_jsRef == null)
                    return;

                var chunk = frame.Data;
                var offset = frame.Offset.TotalSeconds;
                var buffered = await _jsRef.InvokeAsync<double>("appendAudio", cancellationToken, chunk, offset);

                if (buffered > 10) {
                    await using var _ = cancellationToken.Register(() => _delayTokenSource.Cancel());
                    await Task.Delay(TimeSpan.FromSeconds(5), _delayTokenSource.Token);
                }
            })
            .ConfigureAwait(false);

    private Task CircuitInvoke(Func<Task> workItem)
    {
        try {
            // ReSharper disable once ConditionIsAlwaysTrueOrFalse
            return _circuitContext.IsDisposing || _circuitContext.RootComponent == null
                ? Task.CompletedTask
                : _circuitContext.RootComponent.GetDispatcher().InvokeAsync(workItem);
        }
        catch (Exception e) {
            Log.LogError(e, $"{nameof(CircuitInvoke)}: error processing audio command");
        }
        return Task.CompletedTask;
    }
}
