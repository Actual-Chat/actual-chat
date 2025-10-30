using ActualChat.UI.Blazor;
using ActualChat.UI.Blazor.Services;

namespace ActualChat.App.Maui.Audio;

public class IosTuneUI(UIHub hub) : MauiTunes(hub)
{
    [field: AllowNull, MaybeNull]
    private AudioEngines AudioEngines => field ??= Hub.Services.GetRequiredService<AudioEngines>();
    [field: AllowNull, MaybeNull]
    private Haptics Haptics => field ??= Hub.Services.GetRequiredService<Haptics>();

    public override Task Play(Tune tune, CancellationToken cancellationToken = default)
        => ForegroundTask.Run(() => {
            var (_, sound) = Tunes[tune];
            _ = Vibrate(tune);
            return PlaySound(sound);
        },
        CancellationToken.None);

    public override Task PlayAndWait(Tune tune, CancellationToken cancellationToken = default)
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
