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

    protected override bool CanVibrate => Vibration.Default.IsSupported;

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

    protected override Task PlayInternal(Tune tune)
    {
        _ = ForegroundTask.Run(() => PlayAndWaitInternal(tune), CancellationToken.None);
        return Task.CompletedTask;
    }

    protected override async Task PlayAndWaitInternal(Tune tune)
    {
        if (GetTuneInfo(tune) is not { } info)
            return;

        var vibrateTask = Vibrate(tune, info);
        var playSoundTask = PlaySound(info.Sound);
        await Task.WhenAll(vibrateTask, playSoundTask).ConfigureAwait(false);
    }

    // Protected methods

    protected virtual async Task PlaySound(string soundName)
    {
        if (soundName.IsNullOrEmpty())
            return;

        try {
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
            AsyncAudioPlayer player;
            try {
                player = await playerTask.ConfigureAwait(false);
            }
            catch {
                // A missing/broken sound file caches a faulted task that would otherwise
                // rethrow (and go unobserved) on every future play - drop it so it's retried.
                _players.TryRemove(new KeyValuePair<string, Task<AsyncAudioPlayer>>(soundName, playerTask));
                throw;
            }

            var scope = await TryAcquireAudioFocus().ConfigureAwait(false);
            if (scope is null)
                return;

            // In a finally, because a throw from PlayAsync used to skip the release: the tune's
            // scope then kept the holder non-empty, so the "last scope released" cleanup that
            // abandons focus, restores Mode.Normal and stops SCO never ran until the next tune
            // that happened to succeed.
            try {
                await player.PlayAsync(CancellationToken.None).ConfigureAwait(false);
            }
            finally {
                ReleaseAudioFocus();
            }
        }
        catch (Exception e) {
            Log.LogError(e, "Failed to play sound {SoundName}", soundName);
        }
    }

    protected virtual async Task Vibrate(Tune tune, TuneInfo info)
    {
        var pattern = info.Vibration;
        if (pattern.Length == 0)
            return;

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
