using ActualChat.Audio.UI.Blazor.Components;
using ActualChat.Audio.UI.Blazor.Module;
using ActualChat.MediaPlayback;

namespace ActualChat.Audio.UI.Blazor.Pages;

#pragma warning disable CS0162 // for if (false) { logging }
#pragma warning disable MA0040

public partial class AudioPlayerTestPage : ComponentBase, IAudioPlayerBackend, IDisposable
{
    [Inject]
    private ILogger<AudioPlayerTestPage> _log { get; set; } = null!;
    [Inject]
    private IServiceProvider _services { get; set; } = null!;
    [Inject]
    private IJSRuntime _js { get; set; } = null!;
    [Inject]
    private ITrackPlayerFactory _trackPlayerFactory { get; set; } = null!;

    protected long ObjectCreationDelay;
    protected long StartPlayingDelay;
    protected long InitializeDuration;
    private bool _isPlaying;
    private CancellationTokenSource? _cts;
    private CancellationTokenRegistration _registration;
    private double _offset;
    private string _uri = "https://dev.actual.chat/api/audio/download/audio-record/01FQEXRGK4DA5BACTDTAGMF0D7/0000.webm";
    private AudioSource? _audioSource;
    private string _audioBlobStreamUri = "";

    public Task OnBlockMainThread(int milliseconds)
    {
        _ = _js.InvokeVoidAsync($"{AudioBlazorUIModule.ImportName}.AudioPlayerTestPage.blockMainThread", milliseconds);
        return Task.CompletedTask;
    }

    [JSInvokable]
    public void OnStartPlaying(AudioPlayerTestPageStats statistics)
    {
        _log.LogInformation("OnStartPlaying called");
        StartPlayingDelay = statistics.PlayingStartTime - statistics.ConstructorStartTime;
        ObjectCreationDelay = statistics.ConstructorEndTime - statistics.ConstructorStartTime;
        StateHasChanged();
    }

    public async Task OnToggleClick()
    {
        if (_isPlaying) {
            _log.LogInformation("StopTask playing");
            _cts?.CancelAndDisposeSilently();
            _cts = null;
            _isPlaying = false;
            StateHasChanged();
        }
        else {
            _log.LogInformation("Start playing");
            _isPlaying = true;
            _offset = 0d;
            StartPlayingDelay = 0;
            StateHasChanged();
            _cts = new CancellationTokenSource();
            var audioSource = await CreateAudioSource(_uri, _cts.Token);
            var blazorRef = DotNetObjectReference.Create<IAudioPlayerBackend>(this);
            var stopWatch = Stopwatch.StartNew();
            var jsRef = await _js.InvokeAsync<IJSObjectReference>(
                $"{AudioBlazorUIModule.ImportName}.AudioPlayerTestPage.create",
                _cts.Token,
                blazorRef).ConfigureAwait(true);
#pragma warning disable VSTHRD101, MA0040
            // ReSharper disable once AsyncVoidLambda
            _registration = _cts.Token.Register(async () => {
                try {
                    _log.LogInformation("Playing was cancelled");
                    await jsRef.InvokeVoidAsync("stop", CancellationToken.None).ConfigureAwait(true);
                    await jsRef.DisposeSilentlyAsync().ConfigureAwait(true);
                    if (_registration != default) {
                        await _registration.DisposeAsync().ConfigureAwait(true);
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
            var frames = await audioSource.GetFrames(_cts.Token).ToArrayAsync(_cts.Token).ConfigureAwait(true);
            InitializeDuration = stopWatch.ElapsedMilliseconds;
            foreach (var frame in frames) {
                if (false) {
                    _log.LogInformation(
                        "Send the frame data to JS side ({FrameLength} bytes, offset={FrameOffset}s, duration={FrameDuration}s)",
                         frame.Data.Length,
                         frame.Offset.TotalSeconds,
                         frame.Duration.TotalSeconds);
                }
                _ = jsRef.InvokeVoidAsync("data", _cts.Token, frame.Data)
                    .ConfigureAwait(true);
            }
            if (!_cts.Token.IsCancellationRequested)
                await jsRef.InvokeVoidAsync("end", _cts.Token).ConfigureAwait(true);
        }
    }

    private async Task<AudioSource> CreateAudioSource(string audioUri, CancellationToken cancellationToken)
    {
        if (_audioSource == null || !StringComparer.Ordinal.Equals(_audioBlobStreamUri, audioUri)) {
            var audioDownloader = new AudioDownloader(_services);
            _audioSource = await audioDownloader.Download(new Uri(audioUri), TimeSpan.Zero, cancellationToken);
            _audioBlobStreamUri = audioUri;
        }
        await _audioSource.WhenFormatAvailable.ConfigureAwait(true);
        return _audioSource;
    }

    [JSInvokable]
    public Task OnChangeReadiness(bool isBufferReady) => Task.CompletedTask;

    [JSInvokable]
    public async Task OnPlayEnded(string? errorMessage)
    {
        _log.LogInformation("OnPlayEnded(msg:{ErrorMessage})", errorMessage);
        // might run stop()  after end(), we shouldn't do this, fix it later
        _cts?.CancelAndDisposeSilently();
        if (_registration != default) {
            await _registration.DisposeAsync().ConfigureAwait(true);
        }
    }

    [JSInvokable]
    public Task OnPlayTimeChanged(double offset)
    {
        if (true) {
            _log.LogInformation("OnPlayTimeChanged(offset={Offset}s)", offset);
        }
        _offset = offset;
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

    public record AudioPlayerTestPageStats(
        long ConstructorStartTime,
        long ConstructorEndTime,
        long PlayingStartTime
    );
}
