using ActualChat.Audio;
using Google.Cloud.Speech.V2;

namespace ActualChat.Transcription.Google;

public class GoogleTranscribeState
{
    private Transcript _stable = Transcript.Empty;
    private float _processedAudioDuration;

    public AudioSource AudioSource { get; }
    public SpeechClient.StreamingRecognizeStream RecognizeStream { get; }
    public ChannelWriter<Transcript> Output { get; }

    public Transcript Unstable { get; set; } = Transcript.Empty;
    public Transcript Stable {
        get => _stable;
        set => Unstable = _stable = value;
    }

    public float ProcessedAudioDuration {
        get => Volatile.Read(ref _processedAudioDuration);
        set => Interlocked.Exchange(ref _processedAudioDuration, value);
    }

    public GoogleTranscribeState(
        AudioSource audioSource,
        SpeechClient.StreamingRecognizeStream recognizeStream,
        ChannelWriter<Transcript> output)
    {
        AudioSource = audioSource;
        RecognizeStream = recognizeStream;
        Output = output;
    }

    public Transcript Stabilize()
        => Stable = Unstable;

    public Transcript Append(bool isStable, string suffix, float? suffixEndTime = null)
        => isStable
            ? Stable = Append(suffix, suffixEndTime)
            : Unstable = Append(suffix, suffixEndTime);

    public Transcript Append(bool isStable, string suffix, LinearMap suffixTextToTimeMap)
        => isStable
            ? Stable = Append(suffix, suffixTextToTimeMap)
            : Unstable = Append(suffix, suffixTextToTimeMap);

    // Private methods

    private Transcript Append(string suffix, float? suffixEndTime = null)
        => Stable.WithSuffix(suffix, Unstable.TimeMap, suffixEndTime);

    private Transcript Append(string suffix, LinearMap suffixTextToTimeMap)
        => Stable.WithSuffix(suffix, suffixTextToTimeMap);
}
