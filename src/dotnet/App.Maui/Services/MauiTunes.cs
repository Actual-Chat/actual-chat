using ActualChat.UI.Blazor;
using ActualChat.UI.Blazor.App.Services;
using ActualChat.UI.Blazor.Services;
using Plugin.Maui.Audio;
using AudioManager = Plugin.Maui.Audio.AudioManager;

namespace ActualChat.App.Maui;

public class MauiTunes : TuneUI
{
    private readonly ConcurrentDictionary<string, Task<AsyncAudioPlayer>> _players = new(StringComparer.Ordinal);
    private readonly AudioFocusConsumer _audioFocusConsumer;
    private IAudioFocusActivation? _audioFocusActivation;

    private AudioFocusService AudioFocusService => ((AppUIHub)Hub).AudioFocusService;

    public MauiTunes(UIHub hub) : base(hub)
        => _audioFocusConsumer = new AudioFocusConsumer(AudioMode.Tunes, OnLostFocus);

    public override void Dispose()
    {
        base.Dispose();
        foreach (var playerTask in _players.Values) {
            if (!playerTask.IsCompletedSuccessfully)
                continue;

            try {
 #pragma warning disable VSTHRD002
                playerTask.Result.Dispose();
 #pragma warning restore VSTHRD002
            }
            catch {
                /* ignore dispose errors */
            }
        }
        _players.Clear();
    }

    public override Task Play(Tune tune, CancellationToken cancellationToken = default)
    {
        _ = ForegroundTask.Run(() => PlayAndWait(tune, CancellationToken.None), CancellationToken.None);
        return Task.CompletedTask;
    }

    public override async Task PlayAndWait(Tune tune, CancellationToken cancellationToken = default)
    {
        var audioService = AudioManager.Current;
        if (!Tunes.TryGetValue(tune, out var info))
            return;

        var vibrateTask = Vibrate(tune);
        var playSoundTask = PlaySound();
        await Task.WhenAll(vibrateTask, playSoundTask).ConfigureAwait(false);
        return;

        async Task PlaySound()
        {
            if (info.Sound.IsNullOrEmpty())
                return;

            var playerTask = _players.GetOrAdd(info.Sound,
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
            await PlayInternal(player).ConfigureAwait(true);
        }

        async Task PlayInternal(AsyncAudioPlayer player)
        {
            var audioFocusActivation = await TryGainAudioFocus().ConfigureAwait(false);
            if (audioFocusActivation is null)
                return;

            await player.PlayAsync(cancellationToken).ConfigureAwait(false);
            ReleaseAudioFocus();
        }
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

    private async Task<IAudioFocusActivation?> TryGainAudioFocus()
    {
        if (_audioFocusActivation is not null && !_audioFocusActivation.IsSuspended) {
            Log.LogInformation("Already have audio focus Id={Id}", _audioFocusActivation.Id);
            return _audioFocusActivation;
        }
        _audioFocusActivation = await AudioFocusService.TryGainAudioFocus(_audioFocusConsumer).ConfigureAwait(false);
        return _audioFocusActivation;
    }

    private void ReleaseAudioFocus()
    {
        _audioFocusActivation?.Release();
        _audioFocusActivation = null;
    }

    private RestoreFocusHandler? OnLostFocus(bool mayRecover)
    {
        if (!mayRecover)
            _audioFocusActivation = null;
        return null;
    }
}
