using ActualChat.Audio;
using ActualChat.Streaming.UI.Blazor.Components;
using ActualChat.MediaPlayback;
using ActualChat.Streaming.UI.Blazor.Module;

namespace ActualChat.Streaming.UI.Blazor.Pages;

#pragma warning disable CS0162 // for if (false) { logging }
#pragma warning disable MA0040

public partial class AudioPlayerTestPage : ComponentBase, IAudioPlayerBackend, IDisposable
{
    private bool _isPlaying;
    private bool _isPaused;
    private IJSObjectReference? _jsRef;
    private CancellationTokenSource? _cts;
    private CancellationTokenRegistration _registration;
    private double _offset;
    private string _uri = "https://dev.actual.chat/api/audio/download/audio-record/01FQEXRGK4DA5BACTDTAGMF0D7/0000.webm";
    private AudioSource? _audioSource;
    private string _audioBlobStreamUri = "";

    [Inject] private IServiceProvider Services { get; init; } = null!;
    [Inject] private IJSRuntime JS { get; init; } = null!;
    [Inject] private ITrackPlayerFactory TrackPlayerFactory { get; init; } = null!;
    [Inject] private ILogger<AudioPlayerTestPage> Log { get; init; } = null!;

    protected long ObjectCreationDelay;
    protected long StartPlayingDelay;
    protected long InitializeDuration;

    public Task OnBlockMainThread(int milliseconds)
    {
        _ = JS.InvokeVoidAsync($"{StreamingBlazorUIModule.ImportName}.AudioPlayerTestPage.blockMainThread", milliseconds);
        return Task.CompletedTask;
    }

    [JSInvokable]
    public void OnPlaying(AudioPlayerTestPageStats statistics)
    {
        Log.LogInformation("OnPlaying called");
        StartPlayingDelay = statistics.PlayingStartTime - statistics.ConstructorStartTime;
        ObjectCreationDelay = statistics.ConstructorEndTime - statistics.ConstructorStartTime;
        StateHasChanged();
    }

    public async Task OnToggleClick()
    {
        if (_isPlaying) {
            Log.LogInformation("StopTask playing");
            _cts.CancelAndDisposeSilently();
            _cts = null;
            _isPlaying = false;
            StateHasChanged();
        }
        else {
            Log.LogInformation("Start playing");
            _isPlaying = true;
            _offset = 0d;
            StartPlayingDelay = 0;
            StateHasChanged();
            _cts = new CancellationTokenSource();
            var audioSource = await CreateAudioSource(_uri, _cts.Token);
            var blazorRef = DotNetObjectReference.Create<IAudioPlayerBackend>(this);
            var startedAt = CpuTimestamp.Now;
            _jsRef = await JS.InvokeAsync<IJSObjectReference>(
                $"{StreamingBlazorUIModule.ImportName}.AudioPlayerTestPage.create",
                _cts.Token,
                blazorRef);
#pragma warning disable VSTHRD101, MA0040, MA0147
            // ReSharper disable once AsyncVoidLambda
            _registration = _cts.Token.Register(async () => {
                try {
                    Log.LogInformation("Playing was cancelled");
                    await _jsRef.InvokeVoidAsync("stop", CancellationToken.None);
                    await _jsRef.DisposeSilentlyAsync();
                    if (_registration != default) {
                        await _registration.DisposeAsync();
                    }
                }
                catch (Exception ex) {
                    Log.LogError(ex, "Dispose registration error");
                }
                finally {
                    _isPlaying = false;
                    _isPaused = false;
                    _registration = default;
                    StateHasChanged();
                }
            });
            var frames = await audioSource.GetFrames(_cts.Token).ToListAsync(_cts.Token);
            InitializeDuration = (long)startedAt.Elapsed.TotalMilliseconds;
            foreach (var frame in frames) {
                if (false) {
                    Log.LogInformation(
                        "Send the frame data to JS side ({FrameLength} bytes, offset={FrameOffset}s, duration={FrameDuration}s)",
                         frame.Data.Length,
                         frame.Offset.TotalSeconds,
                         frame.Duration.TotalSeconds);
                }
                _ = _jsRef.InvokeVoidAsync("frame", _cts.Token, frame.Data);
            }
            if (!_cts.Token.IsCancellationRequested)
                await _jsRef.InvokeVoidAsync("end", _cts.Token);
        }
    }

    private async Task OnPauseToggleClick()
    {
        if (!_isPlaying)
            return;
        await _jsRef!.InvokeVoidAsync(_isPaused ? "resume" : "pause");
        _isPaused = !_isPaused;
    }

    private async Task OnDecoderLeakTestClick()
    {
        if (_jsRef == null) {
            _cts = new CancellationTokenSource();
            var blazorRef = DotNetObjectReference.Create<IAudioPlayerBackend>(this);
            _jsRef = await JS.InvokeAsync<IJSObjectReference>(
                $"{StreamingBlazorUIModule.ImportName}.AudioPlayerTestPage.create",
                _cts.Token,
                blazorRef);
        }
        await _jsRef.InvokeVoidAsync("testDecoder");
    }

    private async Task<AudioSource> CreateAudioSource(string audioBlobUrl, CancellationToken cancellationToken)
    {
        if (_audioSource == null || !OrdinalEquals(_audioBlobStreamUri, audioBlobUrl)) {
            var audioDownloader = new AudioDownloader(Services);
            _audioSource = await audioDownloader.Download(audioBlobUrl, TimeSpan.Zero, cancellationToken);
            _audioBlobStreamUri = audioBlobUrl;
        }
        return _audioSource;
    }

    [JSInvokable]
    public Task OnPlaying(double offset, bool isPaused, bool isBufferLow)
    {
        var playing = isPaused ? "paused" : "playing";
        var buffer = isBufferLow ? "low" : "ok";
        // ReSharper disable once TemplateIsNotCompileTimeConstantProblem
        Log.LogInformation($"OnPlaying: {playing} @ {{Offset}}, buffer: {buffer}", offset);

        _offset = offset;
        StateHasChanged();
        return Task.CompletedTask;
    }

    [JSInvokable]
    public async Task OnEnded(string? errorMessage)
    {
        Log.LogInformation("OnEnded: {ErrorMessage}", errorMessage);
        _cts.CancelAndDisposeSilently();
        if (_registration != default)
            await _registration.DisposeAsync();
    }

    public void Dispose()
    {
        if (_registration != default) {
            _registration.Dispose();
            _registration = default;
        }
        _cts.CancelAndDisposeSilently();
        GC.SuppressFinalize(this);
    }

    public record AudioPlayerTestPageStats(
        long ConstructorStartTime,
        long ConstructorEndTime,
        long PlayingStartTime
    );
}
