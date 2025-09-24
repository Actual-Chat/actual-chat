using AVFoundation;
using Foundation;

namespace ActualChat.App.Maui.Playback;

public class SoundPlayerNode : IDisposable
{
    private AVAudioEngine Engine { get; }
    private AVAudioPlayerNode Node { get; }

    public SoundPlayerNode(AVAudioEngine engine, AVAudioFormat format)
    {
        Engine = engine;
        Node = new AVAudioPlayerNode();
        engine.AttachNode(Node);
        engine.Connect(Node, engine.MainMixerNode, format);
    }

    public void Dispose()
    {
        Node.Stop();
        Engine.DisconnectNodeInput(Node);
        Engine.DisconnectNodeOutput(Node);
        Engine.DetachNode(Node);
        Node.DisposeSilently();
    }

    public async Task PlayResourceFile(string resourceFileName)
    {
        var nsUrl = NSBundle.MainBundle.GetUrlForResource(resourceFileName, "m4a", "sounds");
        var audioFile = new AVAudioFile(nsUrl, out var nsError);
        nsError.Assert();

        if (!Engine.Running) {
            Engine.Prepare();
            Engine.StartAndReturnError(out nsError);
            nsError.Assert();
        }

        if (!Node.Playing)
            Node.Play();
        await Node.ScheduleFileAsync(audioFile, null, AVAudioPlayerNodeCompletionCallbackType.PlayedBack)
            .ConfigureAwait(false);
    }
}
