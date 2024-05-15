using ActualChat.Audio;
using ActualChat.Transcription;
using Google.Cloud.Speech.V2;

namespace ActualChat.Streaming.Services.Transcribers;

public class GoogleTranscribeState(
    AudioSource audioSource,
    SpeechClient.StreamingRecognizeStream recognizeStream,
    ChannelWriter<Transcript> output)
{
    private Transcript _stable = Transcript.Empty;
    private float _processedAudioDuration;

    public AudioSource AudioSource { get; } = audioSource;
    public SpeechClient.StreamingRecognizeStream RecognizeStream { get; } = recognizeStream;
    public ChannelWriter<Transcript> Output { get; } = output;

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

    public float[] Stability { get; private set; } = [];

    public GoogleTranscribeState MakeStable(bool isStable = true)
    {
        if (isStable)
            Stable = Unstable;
        return this;
    }

    public GoogleTranscribeState Append(string suffix, float? suffixEndTime, bool appendToUnstable = false)
    {
        Unstable = this[appendToUnstable].WithSuffix(suffix, Unstable.TimeMap, suffixEndTime);
        return this;
    }

    public GoogleTranscribeState Append(string suffix, LinearMap suffixTextToTimeMap, bool appendToUnstable = false)
    {
        Unstable = this[appendToUnstable].WithSuffix(suffix, suffixTextToTimeMap);
        return this;
    }

    public GoogleTranscribeState SetMetadata(float[] stability)
    {
        Stability = stability;
        return this;
    }
}
