using ActualChat.UI.Blazor.App.Services;
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
        Tunes = new AudioEngine(AudioMode.Tunes, hub);
        Playback = new AudioEngine(AudioMode.Playback, hub);
        Recording = new AudioEngine(AudioMode.Recording, hub);
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

    public void Resume(AudioMode mode)
    {
        if (mode >= AudioMode.Tunes)
            Tunes.Resume();
        if (mode >= AudioMode.Playback)
            Playback.Resume();
        if (mode >= AudioMode.Recording)
            Recording.Resume();
    }

    private void OnConfigurationChange(object? sender, NSNotificationEventArgs e)
        => Log.LogInformation("Audio engine configuration change");
}
