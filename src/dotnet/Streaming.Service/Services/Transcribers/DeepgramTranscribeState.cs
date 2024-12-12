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

    public DeepgramTranscribeState Append(string suffix, float? suffixEndTime)
    {
        if (string.IsNullOrWhiteSpace(suffix))
            return this;

        var stableText = Stable.Text;
        var prependedSuffix = stableText.IsNullOrEmpty() || stableText.EndsWith(' ')
            ? suffix.Trim()
            : $" {suffix.Trim()}";
        Unstable = Stable.WithSuffix(prependedSuffix, Unstable.TimeMap, suffixEndTime);
        return this;
    }

    public DeepgramTranscribeState Append(string suffix, LinearMap suffixTextToTimeMap)
    {
        if (string.IsNullOrWhiteSpace(suffix))
            return this;

        var stableText = Stable.Text;
        var prependedSuffix = stableText.IsNullOrEmpty() || stableText.EndsWith(' ')
            ? suffix.Trim()
            : $" {suffix.Trim()}";
        Unstable = Stable.WithSuffix(prependedSuffix, suffixTextToTimeMap);
        return this;
    }

    public DeepgramTranscribeState CompleteSentence()
    {
        Unstable = Unstable.WithSuffix(".", Unstable.TimeMap);
        return this;
    }
}
