using AVFoundation;

namespace ActualChat.App.Maui.Playback;

public class MixerNode(AVAudioMixerNode node, Action<AVAudioNode> disposer) : AudioNode(node, disposer), IDisposable
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
