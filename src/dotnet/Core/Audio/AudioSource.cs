using ActualChat.Media;

namespace ActualChat.Audio;

public class AudioSource : MediaSource<AudioFormat, AudioFrame>
{
    public AudioSource(AudioFormat format, IAsyncEnumerable<AudioFrame> frames)
        : base(format, frames) { }
}
