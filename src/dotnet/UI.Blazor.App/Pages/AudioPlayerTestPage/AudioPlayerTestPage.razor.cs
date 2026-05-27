using ActualChat.Audio;
using ActualChat.IO;
using ActualChat.MediaPlayback;
using ActualChat.UI.Blazor.App.Module;

namespace ActualChat.UI.Blazor.App.Pages;

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
    private string _uri = "";
    private AudioSource? _audioSource;
    private string _audioBlobStreamUri = "";

    [Inject] private IServiceProvider Services { get; init; } = null!;
    [Inject] private IHttpClientFactory HttpClientFactory { get; init; } = null!;
    [Inject] private ITrackPlayerFactory TrackPlayerFactory { get; init; } = null!;
    [Inject] private IJSRuntime JS { get; init; } = null!;
    [Inject] private ILogger<AudioPlayerTestPage> Log { get; init; } = null!;

    protected long ObjectCreationDelay;
    protected long StartPlayingDelay;
    protected long InitializeDuration;

    public Task OnBlockMainThread(int milliseconds)
    {
        _ = JS.InvokeVoidAsync($"{BlazorUIAppModule.ImportName}.AudioPlayerTestPage.blockMainThread", milliseconds);
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
                $"{BlazorUIAppModule.ImportName}.AudioPlayerTestPage.create",
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
                _ = _jsRef
                    .ToLogging("testPlayer", Log)
                    .InvokeVoidAsync("frame", _cts.Token, frame.Data.ToArray());
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
                $"{BlazorUIAppModule.ImportName}.AudioPlayerTestPage.create",
                _cts.Token,
                blazorRef);
        }
        await _jsRef.InvokeVoidAsync("testDecoder");
    }

    private async Task<AudioSource> CreateAudioSource(string audioBlobUrl, CancellationToken cancellationToken)
    {
        if (_audioSource != null && _audioBlobStreamUri == audioBlobUrl)
            return _audioSource;

        var clocks = Services.Clocks();
        var audioSourceLog = Services.LogFor<AudioSource>();
        var byteStream = HttpClientFactory.DownloadByteStream(audioBlobUrl.ToUri(), Log, cancellationToken);
        _audioSource = await AudioSource.ReadFromByteStream(byteStream, clocks, audioSourceLog, cancellationToken);
        _audioBlobStreamUri = audioBlobUrl;
        return _audioSource;
    }

    [JSInvokable]
    public void OnPlaying(double offset, bool isPaused, bool isBufferLow)
    {
        var playing = isPaused ? "paused" : "playing";
        var buffer = isBufferLow ? "low" : "ok";
        // ReSharper disable once TemplateIsNotCompileTimeConstantProblem
        Log.LogInformation($"OnPlaying: {playing} @ {{Offset}}, buffer: {buffer}", offset);

        _offset = offset;
        StateHasChanged();
    }

    public void OnPresentationLag(TimeSpan lag) { }

    [JSInvokable]
    public void OnEnded(string? errorMessage)
    {
        Log.LogInformation("OnEnded: {ErrorMessage}", errorMessage);
        _cts.CancelAndDisposeSilently();
        if (_registration != default)
            _ = _registration.DisposeAsync();
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
