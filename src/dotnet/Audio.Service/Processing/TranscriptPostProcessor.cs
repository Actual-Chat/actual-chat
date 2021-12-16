using ActualChat.Transcription;
using Cysharp.Text;

namespace ActualChat.Audio.Processing;

public class TranscriptPostProcessor
{
    public static TimeSpan TranscriptDebouncePeriod { get; set; } = TimeSpan.FromSeconds(0.2);

    private ILogger? _log;
    protected ILogger Log => _log ??= Services.LogFor(GetType());
    protected bool DebugMode => Constants.DebugMode.Transcription;
    protected ILogger? DebugLog => DebugMode ? Log : null;

    protected IServiceProvider Services { get; }
    protected MomentClockSet Clocks { get; }

    public TranscriptPostProcessor(IServiceProvider services)
    {
        Services = services;
        Clocks = Services.Clocks();
    }

    public async IAsyncEnumerable<Transcript> Apply(
        TranscriptSegment transcriptSegment,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var transcripts = transcriptSegment.Suffixes.Reader.ReadAllAsync(cancellationToken);
        transcripts = transcripts.Debounce(TranscriptDebouncePeriod, Clocks.CpuClock, cancellationToken);
        await foreach (var transcript in transcripts.ConfigureAwait(false)) {
            var text = transcript.Text;
            var contentStart = transcript.GetContentStart() - transcript.TextRange.Start;
            if (contentStart == text.Length) {
                yield return transcript;
                continue;
            }

            var firstLetter = text[contentStart];
            var firstLetterUpper = Char.ToUpperInvariant(firstLetter);
            if (firstLetter == firstLetterUpper) {
                yield return transcript;
                continue;
            }

            var newText = ZString.Concat(text[..contentStart], firstLetterUpper, text[(contentStart + 1)..]);
            var newTranscript = new Transcript(newText, transcript.TextToTimeMap, transcript.IsStable);
            yield return newTranscript;
        }
    }
}
