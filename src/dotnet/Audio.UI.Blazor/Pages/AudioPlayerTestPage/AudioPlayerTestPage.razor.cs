using ActualChat.Audio.UI.Blazor.Components;
using ActualChat.Audio.UI.Blazor.Module;

namespace ActualChat.Audio.UI.Blazor.Pages;

#pragma warning disable CS0162 // for if (false) { logging }
#pragma warning disable MA0040

public partial class AudioPlayerTestPage : ComponentBase, IAudioPlayerBackend, IDisposable
{
    [Inject]
    private ILogger<AudioPlayerTestPage> Log { get; set; } = null!;
    [Inject]
    private IServiceProvider Services { get; set; } = null!;
    [Inject]
    private IJSRuntime JS { get; set; } = null!;

    private bool _isPlaying;
    private CancellationTokenSource? _cts;
    private CancellationTokenRegistration _registration;
    private int? _prevMediaElementReadyState;
    private double _offset;
    //private string _uri = "https://dev.actual.chat/api/audio/download/audio-record/01FNWWY0A0VJY3B15VFXA5CGTD/0000.webm";
    private string _uri = "http://localhost:7080/api/audio/download/audio-record/01FP81V6V4HXBWEY22SRXC8V46/0000.webm";
    private const bool DebugMode = true;

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender) {
            StateHasChanged();
        }
        await base.OnAfterRenderAsync(firstRender).ConfigureAwait(true);
    }

    public async Task OnClick(bool isMsePlayer)
    {
        if (_isPlaying) {
            Log.LogInformation("Stop playing");
            _cts?.CancelAndDisposeSilently();
            _cts = null;
            _isPlaying = false;
            StateHasChanged();
        }
        else {
            Log.LogInformation("Start playing");
            _isPlaying = true;
            _offset = 0d;
            _prevMediaElementReadyState = null;
            StateHasChanged();
            _cts = new CancellationTokenSource();
            var audioDownloader = new AudioDownloader(Services);
            var audioSource = await audioDownloader.Download(new Uri(_uri), TimeSpan.Zero, _cts.Token).ConfigureAwait(true);
            var blazorRef = DotNetObjectReference.Create<IAudioPlayerBackend>(this);
            var jsRef = await JS.InvokeAsync<IJSObjectReference>(
                $"{AudioBlazorUIModule.ImportName}.{(isMsePlayer ? "AudioPlayer" : "AudioContextAudioPlayer")}.create",
                _cts.Token, blazorRef, DebugMode
                ).ConfigureAwait(true);
#pragma warning disable VSTHRD101, MA0040
            // ReSharper disable once AsyncVoidLambda
            _registration = _cts.Token.Register(async () => {
                try {
                    Log.LogInformation("Playing was cancelled");
                    await jsRef.InvokeVoidAsync("dispose").ConfigureAwait(true);
                    await jsRef.DisposeSilentlyAsync().ConfigureAwait(true);
                    if (_registration != default) {
                        var reg = _registration;
                        await reg.DisposeAsync().ConfigureAwait(true);
                    }
                }
                catch (Exception ex) {
                    Log.LogError(ex, "Dispose registration error");
                }
                finally {
                    _isPlaying = false;
                    _registration = default;
                    StateHasChanged();
                }
            });
            await jsRef.InvokeVoidAsync("initialize", _cts.Token, audioSource.Format.ToBlobPart().Data).ConfigureAwait(true);
            await foreach (var frame in audioSource.GetFrames(_cts.Token).ConfigureAwait(true)) {
                if (false) {
                    Log.LogInformation(
                        "Send the frame data to js side (bytes: {FrameBytes}, offset sec: {FrameOffset}, duration sec: {FrameDuration})",
                         frame.Data.Length,
                         frame.Offset.TotalSeconds,
                         frame.Duration.TotalSeconds);
                }
                _ = jsRef.InvokeVoidAsync("appendAudioAsync", _cts.Token, frame.Data, frame.Offset.TotalSeconds)
                    .ConfigureAwait(true);
            }
            if (!_cts.Token.IsCancellationRequested)
                _ = jsRef.InvokeVoidAsync("endOfStream").ConfigureAwait(true);
        }
    }

    [JSInvokable]
    public Task OnChangeReadiness(bool isBufferReady, double? offset, int? readyState)
    {
        if (_prevMediaElementReadyState != readyState) {
            _prevMediaElementReadyState = readyState;
            Log.LogInformation(
                "Playing offset~: {PlayingOffset} OnChangeReadiness(isBufferReady: {BufferReadiness}, offset: {Offset}, readyState: {MediaElementReadyState})",
                _offset,
                isBufferReady,
                offset,
                ToMediaElementReadyState(readyState)
            );
            StateHasChanged();
        }
        return Task.CompletedTask;
    }

    private static string ToMediaElementReadyState(int? state) => state switch {
        0 => "HAVE_NOTHING",
        1 => "HAVE_METADATA",
        2 => "HAVE_CURRENT_DATA",
        3 => "HAVE_FUTURE_DATA",
        4 => "HAVE_ENOUGH_DATA",
        null => "null",
        // ReSharper disable once ConstantConditionalAccessQualifier
        _ => $"UNKNOWN:{state?.ToString(CultureInfo.InvariantCulture) ?? "(null)"}",
    };

    [JSInvokable]
    public async Task OnPlaybackEnded(int? errorCode, string? errorMessage)
    {
        Log.LogInformation(
            "OnPlaybackEnded(errorCode: {ErrorCode}, errorMessage: {ErrorMessage})",
            errorCode,
            errorMessage
        );
        _cts?.CancelAndDisposeSilently();
        if (_registration != default) {
            await _registration.DisposeAsync().ConfigureAwait(true);
        }
    }

    [JSInvokable]
    public Task OnPlaybackTimeChanged(double? offset)
    {
        if (false) {
            Log.LogInformation("OnPlaybackTimeChanged(offset: {Offset})", offset);
        }
        _offset = offset ?? 0d;
        StateHasChanged();
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        if (_registration != default) {
            _registration.Dispose();
            _registration = default;
        }
        _cts?.CancelAndDisposeSilently();
        GC.SuppressFinalize(this);
    }
}
