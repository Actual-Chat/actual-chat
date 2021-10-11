using ActualChat.Media;

namespace ActualChat.Audio;

public class AudioSource : MediaSource<AudioFormat, AudioFrame>
{
    public AudioSource(AudioFormat format, Task<TimeSpan> durationTask, AsyncMemoizer<AudioFrame> frameMemoizer)
        : base(format, durationTask, frameMemoizer) { }
}
