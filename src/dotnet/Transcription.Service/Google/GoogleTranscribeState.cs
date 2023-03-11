using System.Text.RegularExpressions;
using ActualChat.Audio;
using Google.Cloud.Speech.V2;

namespace ActualChat.Transcription.Google;

public class GoogleTranscribeState
{
    private static readonly Regex CompleteSentenceOrEmptyRe =
        new(@"([\?\!\.]\s*$)|(^\s*$)", RegexOptions.Compiled | RegexOptions.ExplicitCapture | RegexOptions.Singleline);
    private static readonly Regex EndsWithWhitespaceOrEmptyRe =
        new(@"(\s+$)|(^\s*$)", RegexOptions.Compiled | RegexOptions.ExplicitCapture | RegexOptions.Singleline);

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
            ? Stable = Append(Stable, suffix, suffixEndTime)
            : Unstable = Append(Stable, suffix, suffixEndTime);

    public Transcript Append(bool isStable, string suffix, LinearMap suffixTextToTimeMap)
        => isStable
            ? Stable = Append(Stable, suffix, suffixTextToTimeMap)
            : Unstable = Append(Stable, suffix, suffixTextToTimeMap);

    // Private methods

    public static Transcript Append(Transcript prefix, string suffix, float? suffixEndTime = null)
    {
        suffix = FixSuffix(prefix, suffix);
        return prefix.WithSuffix(suffix, suffixEndTime);
    }

    public static Transcript Append(Transcript prefix, string suffix, LinearMap suffixTextToTimeMap)
    {
        var newSuffix = FixSuffix(prefix, suffix);
        var dSuffixLength = newSuffix.Length - suffix.Length;
        if (dSuffixLength != 0) {
            // The only possible length change is due to either added or removed prefix
            suffixTextToTimeMap = suffixTextToTimeMap.Move(dSuffixLength, 0);
        }
        return prefix.WithSuffix(newSuffix, suffixTextToTimeMap);
    }

    private static string FixSuffix(Transcript prefix, string suffix)
    {
        var firstLetterIndex = Transcript.ContentStartRe.Match(suffix).Length;
        if (firstLetterIndex == suffix.Length)
            return suffix; // Suffix is all whitespace or empty

        var prefixText = prefix.Text;
        if (firstLetterIndex == 0 && !EndsWithWhitespaceOrEmptyRe.IsMatch(prefixText)) {
            // Add spacer
            suffix = " " + suffix;
            firstLetterIndex++;
        }
        else if (firstLetterIndex > 0 && EndsWithWhitespaceOrEmptyRe.IsMatch(prefixText)) {
            // Remove spacer
            suffix = suffix[firstLetterIndex..];
            firstLetterIndex = 0;
        }

        if (CompleteSentenceOrEmptyRe.IsMatch(prefixText))
            suffix = suffix.Capitalize(firstLetterIndex);

        return suffix;
    }
}
