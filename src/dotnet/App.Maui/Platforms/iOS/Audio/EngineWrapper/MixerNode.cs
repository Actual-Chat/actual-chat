using ActualChat.UI.Blazor.App.Services;
using AVFoundation;

namespace ActualChat.App.Maui.Audio;

public class MixerNode(AVAudioMixerNode node, Action<AVAudioNode> disposer, AppUIHub hub)
    : AudioNode(node, disposer, hub), IDisposable
{
    public float Volume {
        get {
            lock (Lock)
                return node.Volume;
        }
        set {
            lock (Lock)
                node.Volume = value;
        }
    }

    public float OutputVolume {
        get {
            lock (Lock)
                return node.OutputVolume;
        }
        set {
            lock (Lock)
                node.OutputVolume = value;
        }
    }
}
