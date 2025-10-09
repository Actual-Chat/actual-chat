using AVFoundation;

namespace ActualChat.App.Maui.Audio;

public class MixerNode(AVAudioMixerNode node, Action<AVAudioNode> disposer, ILogger<MixerNode> log)
    : AudioNode(node, disposer, log), IDisposable
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
