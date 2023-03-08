using ActualChat.Transcription;
using Cysharp.Text;

namespace ActualChat.Audio.Processing;

public sealed class TranscriptPostProcessor : TranscriptionProcessorBase
{
    public static TimeSpan TranscriptDebouncePeriod { get; set; } = TimeSpan.FromSeconds(0.2);

    public TranscriptPostProcessor(IServiceProvider services) : base(services) { }

    public async IAsyncEnumerable<Transcript> Apply(
        TranscriptSegment transcriptSegment,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var transcripts = transcriptSegment.Suffixes.Reader.ReadAllAsync(cancellationToken);
        transcripts = transcripts.Throttle(TranscriptDebouncePeriod, Clocks.CpuClock, cancellationToken);
        await foreach (var transcript in transcripts.ConfigureAwait(false)) {
            var text = transcript.Text;
            var contentStart = transcript.GetContentStart() - transcript.TextRange.Start;
            if (contentStart == text.Length) {
                yield return transcript;
                continue;
            }

            var firstLetter = text[contentStart];
            var firstLetterUpper = char.ToUpperInvariant(firstLetter);
            if (firstLetter == firstLetterUpper) {
                yield return transcript;
                continue;
            }

            var newText = ZString.Concat(text[..contentStart], firstLetterUpper, text[(contentStart + 1)..]);
            var newTranscript = new Transcript(newText, transcript.TimeMap, transcript.IsStable);
            yield return newTranscript;
        }
    }
}
