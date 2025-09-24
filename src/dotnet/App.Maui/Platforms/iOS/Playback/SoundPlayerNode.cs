using ActualChat.UI.Blazor.App.Services;
using AVFoundation;
using Foundation;

namespace ActualChat.App.Maui.Playback;

public class SoundPlayerNode(AVAudioPlayerNode node, AVAudioFormat format, AppUIHub hub) : IDisposable
{
    [field: AllowNull, MaybeNull]
    private AudioNodes Nodes => field ??= hub.Services.GetRequiredService<AudioNodes>();

    public void Dispose()
        => Nodes.DisposeNode(node);

    public async Task PlayResourceFile(string resourceFileName)
    {
        var nsUrl = NSBundle.MainBundle.GetUrlForResource(resourceFileName, "m4a", "sounds");
        var audioFile = new AVAudioFile(nsUrl, out var nsError);
        nsError.Assert();

        Nodes.EnsureNodePlaying(node);
        await node.ScheduleFileAsync(audioFile, null, AVAudioPlayerNodeCompletionCallbackType.PlayedBack)
            .ConfigureAwait(false);
    }
}
