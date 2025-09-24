using AVFoundation;
using Foundation;

namespace ActualChat.App.Maui.Playback;

public static class AudioNodesExt
{
    public static async Task PlayResourceFile(this AudioNodes nodes, string resourceFileName)
    {
        var nsUrl = NSBundle.MainBundle.GetUrlForResource(resourceFileName, "m4a", "sounds");
        var audioFile = new AVAudioFile(nsUrl, out var nsError);
        nsError.Assert();

        using var node = nodes.CreatePlayerNode(audioFile.ProcessingFormat);
        node.Play();
        await node.ScheduleFileAndWait(audioFile).ConfigureAwait(false);
    }
}
