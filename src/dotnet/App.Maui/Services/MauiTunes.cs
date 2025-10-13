using ActualChat.UI.Blazor;
using ActualChat.UI.Blazor.Services;
using Plugin.Maui.Audio;
using AudioManager = Plugin.Maui.Audio.AudioManager;

namespace ActualChat.App.Maui;

public class MauiTunes(UIHub hub) : TuneUI(hub)
{
    private readonly ConcurrentDictionary<string, Task<AsyncAudioPlayer>> _players = new();

    public override void Dispose()
    {
        base.Dispose();
        foreach (var playerTask in _players.Values) {
            if (!playerTask.IsCompletedSuccessfully)
                continue;

            try {
                playerTask.Result.Dispose();
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

        if (!info.Sound.IsNullOrEmpty()) {
            var playerTask = _players.GetOrAdd(info.Sound,
                async sound => {
                    var filePath = $"sounds/{sound}.m4a";
                    var stream = await FileSystem.OpenAppPackageFileAsync(filePath);
                    return audioService.CreateAsyncPlayer(
                        stream,
                        new AudioPlayerOptions {
#if ANDROID
                            AudioContentType = Android.Media.AudioContentType.Sonification,
                            AudioUsageKind = Android.Media.AudioUsageKind.Assistant,
#endif
                        });
                });
            var player = await playerTask.ConfigureAwait(false);
            await player.PlayAsync(cancellationToken);
        }

        await Vibrate(tune).ConfigureAwait(false);
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
}
