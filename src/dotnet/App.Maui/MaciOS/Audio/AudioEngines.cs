using ActualChat.UI.Blazor.App.Services;
using ActualChat.UI.Blazor.Services;
using AVFoundation;
using Foundation;

namespace ActualChat.App.Maui.Audio;

public class AudioEngines : ProcessorBase
{
    private readonly Disposable<NSObject> _configurationChangeSubscription;

    public AudioEngine Tunes { get; }
    public AudioEngine Playback { get; }
    public AudioEngine Recording { get; }

    private AppUIHub Hub { get; }
    private ILogger Log => field ??= Hub.LogFor(GetType());

    public AudioEngines(AppUIHub hub)
    {
        Hub = hub;
        Tunes = new AudioEngine(AudioFocusMode.Tune, hub);
        Playback = new AudioEngine(AudioFocusMode.Playback, hub);
        Recording = new AudioEngine(AudioFocusMode.Recording, hub);
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

    public void Resume(AudioFocusMode mode)
    {
        if (mode >= AudioFocusMode.Tune)
            Tunes.Resume();
        if (mode >= AudioFocusMode.Playback)
            Playback.Resume();
        if (mode >= AudioFocusMode.Recording)
            Recording.Resume();
    }

    private void OnConfigurationChange(object? sender, NSNotificationEventArgs e)
        => Log.LogInformation("Audio engine configuration change");
}
