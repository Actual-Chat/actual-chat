using AVFoundation;
using Foundation;

namespace ActualChat.App.Maui.Playback;

public static class AudioEngineExt
{
    private static readonly ConcurrentDictionary<string, NSUrl> Urls = new (StringComparer.OrdinalIgnoreCase);

    public static async Task PlayResourceFile(this AudioEngine engine, string resourceFileName)
    {
        await Task.Yield();
        var url = Urls.GetOrAdd(resourceFileName, GetUrl);
        using var audioFile = new AVAudioFile(url, out var error);
        error.Assert();

        using var node = engine.NewPlayer(audioFile.ProcessingFormat);
        engine.Prepare();
        engine.EnsureRunning();
        node.Play();
        await node.ScheduleFileAndWait(audioFile).ConfigureAwait(false);
    }

    private static NSUrl GetUrl(string soundName)
        => NSBundle.MainBundle.GetUrlForResource(soundName, "m4a", "sounds");
}
