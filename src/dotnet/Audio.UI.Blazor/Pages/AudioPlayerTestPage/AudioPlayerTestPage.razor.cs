using ActualChat.Audio.UI.Blazor.Components;
using ActualChat.Audio.UI.Blazor.Module;
using Storage.NetCore.Blobs;

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

    protected long ObjectCreationDelay;
    protected long InitializeDelay;
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
        _ = JS.InvokeVoidAsync($"{AudioBlazorUIModule.ImportName}.AudioPlayerTestPage.blockMainThread", milliseconds);
        return Task.CompletedTask;
    }

    [JSInvokable]
    public void OnStartPlaying(AudioPlayerTestPageStats statistics)
    {
        Log.LogInformation("OnStartPlaying called");
        InitializeDelay = statistics.InitializeEndTime - statistics.ConstructorStartTime;
        StartPlayingDelay = statistics.PlayingStartTime - statistics.ConstructorStartTime;
        ObjectCreationDelay = statistics.ConstructorEndTime - statistics.ConstructorStartTime;
        StateHasChanged();
    }

    public async Task OnToggleClick()
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
            InitializeDelay = 0;
            StartPlayingDelay = 0;
            StateHasChanged();
            _cts = new CancellationTokenSource();
            var audioSource = await CreateAudioSource(_uri, _cts.Token);
            var blazorRef = DotNetObjectReference.Create<IAudioPlayerBackend>(this);
            var stopWatch = Stopwatch.StartNew();
            var jsRef = await JS.InvokeAsync<IJSObjectReference>(
                $"{AudioBlazorUIModule.ImportName}.AudioPlayerTestPage.create",
                _cts.Token,
                blazorRef).ConfigureAwait(true);
#pragma warning disable VSTHRD101, MA0040
            // ReSharper disable once AsyncVoidLambda
            _registration = _cts.Token.Register(async () => {
                try {
                    Log.LogInformation("Playing was cancelled");
                    await jsRef.InvokeVoidAsync("stop", CancellationToken.None).ConfigureAwait(true);
                    await jsRef.DisposeSilentlyAsync().ConfigureAwait(true);
                    if (_registration != default) {
                        await _registration.DisposeAsync().ConfigureAwait(true);
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
            _ = jsRef.InvokeVoidAsync("init", audioSource.Format.Serialize());
            var frames = await audioSource.GetFrames(_cts.Token).ToArrayAsync(_cts.Token).ConfigureAwait(true);
            InitializeDuration = stopWatch.ElapsedMilliseconds;
            foreach (var frame in frames) {
                if (false) {
                    Log.LogInformation(
                        "Send the frame data to js side ({FrameLength} bytes, offset={FrameOffset}s, duration={FrameDuration}s)",
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
            var audioDownloader = new AudioDownloader(Services);
            _audioSource = await audioDownloader.Download(new Uri(audioUri), TimeSpan.Zero, cancellationToken);
            _audioBlobStreamUri = audioUri;
        }
        await _audioSource.WhenFormatAvailable.ConfigureAwait(true);
        return _audioSource;
    }

    [JSInvokable]
    public Task OnChangeReadiness(bool isBufferReady) => Task.CompletedTask;

    [JSInvokable]
    public async Task OnPlaybackEnded(string? errorMessage)
    {
        Log.LogInformation("OnPlaybackEnded(errorMessage={ErrorMessage})", errorMessage);
        _cts?.CancelAndDisposeSilently();
        if (_registration != default) {
            await _registration.DisposeAsync().ConfigureAwait(true);
        }
    }

    [JSInvokable]
    public Task OnPlaybackTimeChanged(double offset)
    {
        if (false) {
            Log.LogInformation("OnPlaybackTimeChanged(offset={Offset}s)", offset);
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
        long InitializeStartTime,
        long InitializeEndTime,
        long PlayingStartTime
    );
}
