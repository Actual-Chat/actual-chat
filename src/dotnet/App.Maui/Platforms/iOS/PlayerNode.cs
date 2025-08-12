using AVFoundation;
using Foundation;

namespace ActualChat.App.Maui;

internal class PlayerNode(AVAudioEngine engine, AVAudioPlayerNode node) : IDisposable
{
    public static PlayerNode Create(AVAudioEngine engine, AVAudioFormat format)
    {
        var node = new AVAudioPlayerNode();
        engine.AttachNode(node);
        engine.Connect(node, engine.MainMixerNode, format);
        return new PlayerNode(engine, node);
    }

    public async Task PlayResourceFile(string resourceFileName)
    {
        var nsUrl = NSBundle.MainBundle.GetUrlForResource(resourceFileName, "m4a", "sounds");
        var audioFile = new AVAudioFile(nsUrl, out var nsError);
        nsError.Assert();

        if (!engine.Running) {
            engine.Prepare();
            engine.StartAndReturnError(out nsError);
            nsError.Assert();
        }

        if (!node.Playing)
            node.Play();
        await node.ScheduleFileAsync(audioFile, null, AVAudioPlayerNodeCompletionCallbackType.PlayedBack)
            .ConfigureAwait(false);
    }

    public void Dispose()
        => node.Stop();
}
