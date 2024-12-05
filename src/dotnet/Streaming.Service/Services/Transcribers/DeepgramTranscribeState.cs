using ActualChat.Audio;
using ActualChat.Transcription;

namespace ActualChat.Streaming.Services.Transcribers;

public class DeepgramTranscribeState(
    AudioSource audioSource,
    ChannelWriter<Transcript> output)
{
    public AudioSource AudioSource { get; } = audioSource;
    public ChannelWriter<Transcript> Output { get; } = output;

    public Transcript Unstable { get; private set; } = Transcript.Empty;

    public Transcript Stable {
        get;
        private set => Unstable = field = value;
    } = Transcript.Empty;

    public Transcript this[bool isUnstable]
        => isUnstable ? Unstable : Stable;

    public float ProcessedAudioDuration {
        get => Volatile.Read(ref field);
        set => Interlocked.Exchange(ref field, value);
    }

    public DeepgramTranscribeState MakeStable(bool isStable = true)
    {
        if (isStable)
            Stable = Unstable;
        return this;
    }

    public DeepgramTranscribeState Append(string suffix, float? suffixEndTime, bool appendToUnstable = false)
    {
        Unstable = this[appendToUnstable].WithSuffix(suffix, Unstable.TimeMap, suffixEndTime);
        return this;
    }

    public DeepgramTranscribeState Append(string suffix, LinearMap suffixTextToTimeMap, bool appendToUnstable = false)
    {
        Unstable = this[appendToUnstable].WithSuffix(suffix, suffixTextToTimeMap);
        return this;
    }
}
