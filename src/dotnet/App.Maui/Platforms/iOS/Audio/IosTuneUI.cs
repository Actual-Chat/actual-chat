using ActualChat.App.Maui.Services;
using ActualChat.UI.Blazor;
using ActualChat.UI.Blazor.Services;

namespace ActualChat.App.Maui.Audio;

public sealed class IosTuneUI(UIHub hub) : MauiTuneUI(hub)
{
    private AudioEngines AudioEngines => field ??= Hub.Services.GetRequiredService<AudioEngines>();
    private Haptics Haptics => field ??= Hub.Services.GetRequiredService<Haptics>();

    // Protected methods

    protected override async Task PlaySound(string soundName)
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

    protected override Task Vibrate(Tune tune)
        => BackgroundTask.Run(() => Haptics.Vibrate(tune, Tunes[tune].Vibration), Log, $"Failed to vibrate '{tune}'");
}
