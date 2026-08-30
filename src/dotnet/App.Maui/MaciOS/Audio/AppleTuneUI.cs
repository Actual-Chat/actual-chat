using ActualChat.App.Maui.Services;
using ActualChat.Pooling;
using ActualChat.UI.Blazor;
using ActualChat.UI.Blazor.Services;
using AVFoundation;
using Foundation;

namespace ActualChat.App.Maui.Audio;

public sealed class AppleTuneUI(UIHub hub) : MauiTuneUI(hub)
{
    private static readonly ConcurrentDictionary<string, NSUrl> Urls = new (StringComparer.OrdinalIgnoreCase);
    private readonly SharedResourcePool<string, AVAudioFile> _audioFiles = new (GetAudioFile, ReleaseAudioFile) {
        ResourceDisposeDelay = TimeSpan.FromMinutes(1),
    };

    private AudioEngines AudioEngines => field ??= Hub.Services.GetRequiredService<AudioEngines>();
    private AudioFocusUI AudioFocusUI => field ??= Hub.Services.GetRequiredService<AudioFocusUI>();
#if IOS
    private Haptics Haptics => field ??= Hub.Services.GetRequiredService<Haptics>();
#endif

    // Protected methods

    protected override async Task DisposeAsyncCore()
    {
        await base.DisposeAsyncCore().ConfigureAwait(false);
        await _audioFiles.DisposeSilentlyAsync().ConfigureAwait(false);
    }

    protected override async Task PlaySound(string soundName)
    {
        DebugLog?.LogInformation("PlaySound: '{SoundName}'", soundName);
        if (soundName.IsNullOrEmpty())
            return;

        try {
            var cancellationToken = StopToken;
            var engine = AudioEngines.Tunes;
            using var audioFileLease = await _audioFiles.Rent(soundName, cancellationToken).ConfigureAwait(false);
            var audioFile = audioFileLease.Resource;

            using var node = engine.NewPlayer(audioFile.ProcessingFormat);
            engine.EnsureRunning();
            node.Play();
            // A tune started while the recording engine's VoiceProcessingIO runs is a source
            // started after it, so it lands on the earpiece unless the route is restated - and
            // the recording-confirm tune plays right after that engine starts, every time.
            await AudioFocusUI.EnsureOutputRoute(cancellationToken).ConfigureAwait(false);
            using var cts = cancellationToken.CreateLinkedTokenSource(TimeSpan.FromSeconds(10));
            await node.ScheduleFileAndWait(audioFile, cts.Token).ConfigureAwait(false);
        }
        catch (Exception e) {
            Log.LogError(e, "Failed to play sound {SoundName}", soundName);
        }
    }

#if IOS
    protected override bool CanVibrate
        // Mac Catalyst keeps MauiTuneUI's Vibration.Default check; iOS needs the CoreHaptics one,
        // which is also false on iPads - they have no Taptic Engine.
        => Haptics.IsSupported;

    protected override Task Vibrate(Tune tune, TuneInfo info)
        => info.Vibration.Length == 0
            ? Task.CompletedTask
            : BackgroundTask.Run(() => Haptics.Vibrate(tune, info.Vibration), Log, $"Failed to vibrate '{tune}'");
#endif

    // Private methods

    private static Task<AVAudioFile> GetAudioFile(string resourceFileName, CancellationToken cancellationToken)
    {
        var url = Urls.GetOrAdd(resourceFileName, GetUrl);
        var audioFile = new AVAudioFile(url, out var error);
        error.Assert();
        return Task.FromResult(audioFile);
    }

    private static ValueTask ReleaseAudioFile(string key, AVAudioFile audioFile)
    {
        // audioFile.Close();
        audioFile.DisposeSilently();
        return ValueTask.CompletedTask;
    }

    private static NSUrl GetUrl(string soundName)
        => NSBundle.MainBundle.GetUrlForResource(soundName, "m4a", "sounds").Require();
}
