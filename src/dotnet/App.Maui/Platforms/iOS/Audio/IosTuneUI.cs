using ActualChat.App.Maui.Services;
using ActualChat.UI.Blazor;
using ActualChat.UI.Blazor.Services;

namespace ActualChat.App.Maui.Audio;

public sealed class IosTuneUI(UIHub hub) : MauiTuneUI(hub)
{
    private AudioEngines AudioEngines => field ??= Hub.Services.GetRequiredService<AudioEngines>();
    private Haptics Haptics => field ??= Hub.Services.GetRequiredService<Haptics>();

    public override Task Play(Tune tune)
        => ForegroundTask.Run(() => {
                DebugLog?.LogInformation("Play: '{Tune}'", tune);
                var (_, sound) = Tunes[tune];
                _ = Vibrate(tune);
                return PlaySound(sound);
            },
            Log,
            $"Failed to play '{tune}'",
            StopToken);

    public override Task PlayAndWait(Tune tune)
    {
        var (_, sound) = Tunes[tune];
        return Task.WhenAll(Vibrate(tune), PlaySound(sound));
    }

    // Protected methods

    protected override Task Vibrate(Tune tune)
        => BackgroundTask.Run(() => Haptics.Vibrate(tune, Tunes[tune].Vibration), Log, $"Failed to vibrate '{tune}'");

    // Private methods

    private async Task PlaySound(string soundName)
    {
        DebugLog?.LogInformation("PlaySound: '{SoundName}'", soundName);
        if (soundName.IsNullOrEmpty())
            return;

        try {
            await AudioEngines.Tunes.PlayResourceFile(soundName).ConfigureAwait(false);
        }
        catch (Exception e) {
            Log.LogError(e, "Failed to play sound {SoundName}", soundName);
        }
    }
}
