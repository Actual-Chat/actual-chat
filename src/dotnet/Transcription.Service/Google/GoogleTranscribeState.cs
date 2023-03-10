using ActualChat.Audio;
using Google.Cloud.Speech.V2;

namespace ActualChat.Transcription.Google;

public class GoogleTranscribeState
{
    public AudioSource AudioSource { get; }
    public SpeechClient.StreamingRecognizeStream RecognizeStream { get; }
    public ChannelWriter<Transcript> Output { get; }

    public Transcript Unstable { get; private set; } = Transcript.Empty;
    public Transcript Stable { get; private set; } = Transcript.Empty;

    public GoogleTranscribeState(
        AudioSource audioSource,
        SpeechClient.StreamingRecognizeStream recognizeStream,
        ChannelWriter<Transcript> output)
    {
        AudioSource = audioSource;
        RecognizeStream = recognizeStream;
        Output = output;
    }

    public Transcript MarkStable()
        => Update(Unstable, true);

    public Transcript AppendUnstable(string suffix, float? suffixEndTime)
        => Update(Stable.WithSuffix(suffix, suffixEndTime), false);

    public Transcript AppendStable(string suffix, float? suffixEndTime)
        => Update(Stable.WithSuffix(suffix, suffixEndTime), true);

    public Transcript AppendStable(string suffix, LinearMap suffixTextToTimeMap)
        => Update(Stable.WithSuffix(suffix, suffixTextToTimeMap), true);

    // Private methods

    private Transcript Update(Transcript next, bool isStable)
    {
        Unstable = next;
        if (isStable)
            Stable = next;
        return Unstable;
    }
}
