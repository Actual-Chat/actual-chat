using ActualChat.Audio;
using Deepgram.Interfaces;
using Google.Cloud.Speech.V2;

namespace ActualChat.Transcription.Deepgram;

public class DeepgramTranscribeState(
    AudioSource audioSource,
    ILiveTranscriptionClient deepgramLive,
    ChannelWriter<Transcript> output)
{
    private Transcript _stable = Transcript.Empty;
    private float _processedAudioDuration;

    public AudioSource AudioSource { get; } = audioSource;
    public ILiveTranscriptionClient DeepgramLive { get; } = deepgramLive;
    public ChannelWriter<Transcript> Output { get; } = output;

    public bool IsLastAppendStable { get; private set; }
    public Transcript Unstable { get; set; } = Transcript.Empty;
    public Transcript Stable {
        get => _stable;
        set => Unstable = _stable = value;
    }
    public Transcript this[bool isUnstable]
        => isUnstable ? Unstable : Stable;

    public float ProcessedAudioDuration {
        get => Volatile.Read(ref _processedAudioDuration);
        set => Interlocked.Exchange(ref _processedAudioDuration, value);
    }

    public DeepgramTranscribeState MakeStable(bool isStable = true)
    {
        if (isStable)
            Stable = Unstable;
        IsLastAppendStable = true;
        return this;
    }

    public DeepgramTranscribeState Append(string suffix, float? suffixEndTime, bool appendToUnstable = false)
    {
        Unstable = this[appendToUnstable].WithSuffix(suffix, Unstable.TimeMap, suffixEndTime);
        IsLastAppendStable = !appendToUnstable;
        return this;
    }

    public DeepgramTranscribeState Append(string suffix, LinearMap suffixTextToTimeMap, bool appendToUnstable = false)
    {
        Unstable = this[appendToUnstable].WithSuffix(suffix, suffixTextToTimeMap);
        IsLastAppendStable = !appendToUnstable;
        return this;
    }
}
