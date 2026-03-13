using ActualChat.UI.Blazor;
using ActualChat.UI.Blazor.Services;
using Plugin.Maui.Audio;
using AudioManager = Plugin.Maui.Audio.AudioManager;

namespace ActualChat.App.Maui.Services;

public class MauiTuneUI : TuneUI
{
    private readonly ConcurrentDictionary<string, Task<AsyncAudioPlayer>> _players = new();
    private readonly AudioFocusRequester _audioFocusRequester;
    private AudioFocusScope? _audioFocusScope;

    private AudioFocusUI AudioFocusUI => Hub.AudioFocusUI;

    public MauiTuneUI(UIHub hub) : base(hub)
        => _audioFocusRequester = new AudioFocusRequester(AudioFocusMode.Tune, OnLostAudioFocus);

    protected override async Task DisposeAsyncCore()
    {
        await base.DisposeAsyncCore().ConfigureAwait(false);
        foreach (var playerTask in _players.Values) {
            if (!playerTask.IsCompletedSuccessfully)
                continue;

            try {
                var player = await playerTask.ConfigureAwait(false);
                player.DisposeSilently();
            }
            catch {
                /* ignore dispose errors */
            }
        }
        _players.Clear();
    }

    public override Task Play(Tune tune)
    {
        _ = ForegroundTask.Run(() => PlayAndWait(tune), CancellationToken.None);
        return Task.CompletedTask;
    }

    public override async Task PlayAndWait(Tune tune)
    {
        if (!Tunes.TryGetValue(tune, out var info))
            return;

        var vibrateTask = Vibrate(tune);
        var playSoundTask = PlaySound(info.Sound);
        await Task.WhenAll(vibrateTask, playSoundTask).ConfigureAwait(false);
    }

    // Protected methods

    protected virtual async Task PlaySound(string soundName)
    {
        if (soundName.IsNullOrEmpty())
            return;

        var audioService = AudioManager.Current;
        var playerTask = _players.GetOrAdd(soundName,
            async sound => {
                var filePath = $"sounds/{sound}.m4a";
                var stream = await FileSystem.OpenAppPackageFileAsync(filePath).ConfigureAwait(false);
                return audioService.CreateAsyncPlayer(
                    stream,
                    new AudioPlayerOptions {
#if ANDROID
                        AudioContentType = Android.Media.AudioContentType.Sonification,
                        AudioUsageKind = Android.Media.AudioUsageKind.AssistanceSonification,
#endif
                    });
            });
        var player = await playerTask.ConfigureAwait(false);

        var scope = await TryAcquireAudioFocus().ConfigureAwait(false);
        if (scope is null)
            return;

        await player.PlayAsync(CancellationToken.None).ConfigureAwait(false);
        ReleaseAudioFocus();
    }

    protected virtual async Task Vibrate(Tune tune)
    {
        if (!Tunes.TryGetValue(tune, out var info))
            return;

        var pattern = info.Vibration;
        var vibration = Vibration.Default;
        if (!vibration.IsSupported)
            return;

        var isDelay = false;
        foreach (var duration in pattern) {
            if (isDelay) {
                await Task.Delay(TimeSpan.FromMilliseconds(duration)).ConfigureAwait(false);
                isDelay = false;
                continue;
            }
            vibration.Vibrate(duration);
            isDelay = true;
        }
    }

    // Private methods

    private async Task<AudioFocusScope?> TryAcquireAudioFocus()
    {
        if (_audioFocusScope is not null && !_audioFocusScope.IsSuspended) {
            Log.LogInformation("Already have audio focus {Scope}", _audioFocusScope);
            return _audioFocusScope;
        }
        _audioFocusScope = await AudioFocusUI.TryAcquire(_audioFocusRequester).ConfigureAwait(false);
        return _audioFocusScope;
    }

    private void ReleaseAudioFocus()
    {
        _audioFocusScope?.Dispose();
        _audioFocusScope = null;
    }

    private AudioFocusRestoreHandler? OnLostAudioFocus(bool mayRecover, bool canDuck)
    {
        if (!mayRecover)
            _audioFocusScope = null;
        return null;
    }
}
