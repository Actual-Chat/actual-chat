using AVFoundation;
using Foundation;

namespace ActualChat.App.Maui.Audio;

public static class AudioEngineExt
{
    private static readonly ConcurrentDictionary<string, NSUrl> Urls = new (StringComparer.OrdinalIgnoreCase);

    public static Task PlayResourceFile(this AudioEngine engine, string resourceFileName)
        => BackgroundTask.Run(async () => {
            var url = Urls.GetOrAdd(resourceFileName, GetUrl);
            using var audioFile = new AVAudioFile(url, out var error);
            error.Assert();

            using var node = engine.NewPlayer(audioFile.ProcessingFormat);
            engine.EnsureRunning();
            node.Play();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            var cancellationToken = cts.Token;
            await node.ScheduleFileAndWait(audioFile, cancellationToken).ConfigureAwait(false);
            node.Stop();
        });

    private static NSUrl GetUrl(string soundName)
        => NSBundle.MainBundle.GetUrlForResource(soundName, "m4a", "sounds");

    public static void StopRecording(this AudioEngine engine)
    {
        engine.Input.Reset();
        engine.Stop();
    }
}
