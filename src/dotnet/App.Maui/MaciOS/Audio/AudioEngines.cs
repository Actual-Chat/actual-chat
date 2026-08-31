using ActualChat.UI.Blazor.App.Services;
using ActualChat.UI.Blazor.Services;
using AVFoundation;
using Foundation;

namespace ActualChat.App.Maui.Audio;

public sealed class AudioEngines : ProcessorBase
{
    // A tune is a one-off sound, so its engine only has to outlive a burst of them.
    private static readonly TimeSpan TuneIdleReleaseDelay = TimeSpan.FromSeconds(1);
    // Gaps between utterances are normal mid-call, and rebuilding across one would put the
    // engine's construction and start latency at the head of the next.
    private static readonly TimeSpan PlaybackIdleReleaseDelay = TimeSpan.FromSeconds(5);

    private readonly Disposable<NSObject> _configurationChangeSubscription;

    private AppUIHub Hub { get; }
    private ILogger Log => field ??= Hub.LogFor(GetType());

    public AudioEngine Tunes { get; }
    public AudioEngine Playback { get; }
    public AudioEngine Recording { get; }

    public AudioEngines(AppUIHub hub)
    {
        Hub = hub;
        Tunes = new AudioEngine(AudioFocusMode.Tune, hub, TuneIdleReleaseDelay);
        Playback = new AudioEngine(AudioFocusMode.Playback, hub, PlaybackIdleReleaseDelay);
        // Recording has no player nodes - it's released explicitly, when the capture ends. Its
        // silent output is the exception, and stays off that list so it can't trigger a release.
        Recording = new AudioEngine(AudioFocusMode.Recording, hub, hasSilentOutput: true);
        _configurationChangeSubscription =
            Disposable.New(AVAudioEngine.Notifications.ObserveConfigurationChange(OnConfigurationChange),
                NSNotificationCenter.DefaultCenter.RemoveObserver);
    }

    protected override async Task DisposeAsyncCore()
    {
        _configurationChangeSubscription.DisposeSilently();
        Tunes.DisposeSilently();
        Playback.DisposeSilently();
        Recording.DisposeSilently();
        await base.DisposeAsyncCore().ConfigureAwait(false);
    }

    public void Pause()
    {
        Tunes.Pause();
        Playback.Pause();
        Recording.Pause();
    }

    public void Release()
    {
        // Apple's contract after a media-services reset: the engines are invalid and have to be
        // recreated, not restarted. Releasing them is what makes the next use build fresh ones.
        Tunes.Release();
        Playback.Release();
        Recording.Release();
    }

    public void Resume(AudioFocusMode mode)
    {
        // Resume() is a no-op on an engine that was stopped rather than paused, so this only
        // revives the ones that still have a live consumer.
        if (mode >= AudioFocusMode.Tune)
            Tunes.Resume();
        if (mode >= AudioFocusMode.Listening)
            Playback.Resume();
        if (mode >= AudioFocusMode.Recording)
            Recording.Resume();
    }

    public void Reconnect(AudioFocusMode mode)
    {
        // Same gating as Resume: this replaces it on the configuration-change path, where an
        // engine that stopped itself also needs its output graph and player nodes restored.
        if (mode >= AudioFocusMode.Tune)
            Tunes.Reconnect();
        if (mode >= AudioFocusMode.Listening)
            Playback.Reconnect();
        if (mode >= AudioFocusMode.Recording)
            Recording.Reconnect();
    }

    // Private methods

    private void OnConfigurationChange(object? sender, NSNotificationEventArgs e)
        => Log.LogInformation("Audio engine configuration change");
}
