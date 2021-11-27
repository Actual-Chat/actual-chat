using ActualChat.Audio.UI.Blazor.Components;
using ActualChat.Playback;
using ActualChat.UI.Blazor;
using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace ActualChat.Audio.UI.Blazor;

public partial class AudioPlayerTestPage : ComponentBase, IAudioPlayerBackend, IDisposable
{
    [Inject]
    private ILogger<AudioPlayerTestPage> _log { get; set; } = null!;

    [Inject]
    private IHttpClientFactory _httpClientFactory { get; set; } = null!;

    [Inject]
    private ILoggerFactory _loggerFactory { get; set; } = null!;

    [Inject]
    private IJSRuntime _js { get; set; } = null!;

    private bool _isPlaying;

    private CancellationTokenSource? _cts = null;
    private CancellationTokenRegistration _registration;
    private int? _prevMediaElementReadyState = null;
    private double _offset;

    public async Task OnClick()
    {
        if (_isPlaying) {
            _log.LogInformation("Stop playing");
            _cts?.CancelAndDisposeSilently();
            _cts = null;
            _isPlaying = false;
            StateHasChanged();
        }
        else {
            _log.LogInformation("Start playing");
            _isPlaying = true;
            _offset = 0d;
            _prevMediaElementReadyState = null;
            StateHasChanged();
            _cts = new CancellationTokenSource();
            const string uri = "https://dev.actual.chat/api/audio/download/audio-record/01FNB06R1YHJCT793JV1CVD79M/0000.webm";
            const bool debugMode = true;
            var audioDownloader = new AudioDownloader(_httpClientFactory, _loggerFactory);
            var audioSource = await audioDownloader.Download(new Uri(uri), TimeSpan.Zero, _cts.Token).ConfigureAwait(true);
            var blazorRef = DotNetObjectReference.Create<IAudioPlayerBackend>(this);
            var jsRef = await _js.InvokeAsync<IJSObjectReference>(
                $"{AudioBlazorUIModule.ImportName}.AudioPlayer.create",
                _cts.Token,
                blazorRef,
                debugMode).ConfigureAwait(true);
#pragma warning disable VSTHRD101, MA0040
            _registration = _cts.Token.Register(async () => {
                try {
                    _log.LogInformation("Playing was cancelled");
                    await jsRef.InvokeVoidAsync("dispose").ConfigureAwait(true);
                    await jsRef.DisposeSilentlyAsync().ConfigureAwait(true);
                    if (_registration != default) {
                        var reg = _registration;
                        await reg.DisposeAsync().ConfigureAwait(true);
                    }
                }
                catch (Exception ex) {
                    _log.LogError(ex, "Dispose registration error");
                }
                finally {
                    _isPlaying = false;
                    _registration = default;
                    StateHasChanged();
                }
            });
            await jsRef.InvokeVoidAsync("initialize", _cts.Token, audioSource.Format.ToBlobPart().Data).ConfigureAwait(true);
            await foreach (var frame in audioSource.GetFrames(_cts.Token)) {
                if (false) {
                    _log.LogInformation(
                        "Send the frame data to js side (bytes: {FrameBytes}, offset sec: {FrameOffset}, duration sec: {FrameDuration})",
                         frame.Data.Length,
                         frame.Offset.TotalSeconds,
                         frame.Duration.TotalSeconds);
                }
                _ = jsRef.InvokeVoidAsync("appendAudioAsync", _cts.Token, frame.Data, frame.Offset.TotalSeconds);
            }
            if (!_cts.Token.IsCancellationRequested)
                _ = jsRef.InvokeVoidAsync("endOfStream");
        }
    }
    [JSInvokable]
    public Task OnChangeReadiness(bool isBufferReady, double? offset, int? readyState)
    {
        if (_prevMediaElementReadyState != readyState) {
            _prevMediaElementReadyState = readyState;
            _log.LogInformation(
                "Playing offset~: {PlayingOffset} OnChangeReadiness(isBufferReady: {BufferReadiness}, offset: {Offset}, readyState: {MediaElementReadyState})",
                _offset,
                isBufferReady,
                offset,
                ToMediaElementReadyState(readyState)
            );
        }
        return Task.CompletedTask;
    }

    string ToMediaElementReadyState(int? state) => state switch {
        0 => "HAVE_NOTHING",
        1 => "HAVE_METADATA",
        2 => "HAVE_CURRENT_DATA",
        3 => "HAVE_FUTURE_DATA",
        4 => "HAVE_ENOUGH_DATA",
        null => "null",
        _ => $"UNKNOWN:{state?.ToString(CultureInfo.InvariantCulture) ?? "(null)"}",
    };

    [JSInvokable]
    public async Task OnPlaybackEnded(int? errorCode, string? errorMessage)
    {
        _log.LogInformation(
            "OnPlaybackEnded(errorCode: {ErrorCode}, errorMessage: {ErrorMessage})",
            errorCode,
            errorMessage
        );
        if (_registration != default) {
            await _registration.DisposeAsync().ConfigureAwait(true);
        }
        _cts?.CancelAndDisposeSilently();
    }

    [JSInvokable]
    public Task OnPlaybackTimeChanged(double? offset)
    {
        if (false) {
            _log.LogInformation("OnPlaybackTimeChanged(offset: {Offset})", offset);
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
    }
}