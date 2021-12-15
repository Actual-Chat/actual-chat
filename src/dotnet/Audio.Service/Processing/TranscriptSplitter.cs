using ActualChat.Transcription;

namespace ActualChat.Audio.Processing;

public class TranscriptSplitter
{
    public static float SplitPauseDuration { get; set; } = 0.5f;
    public static float SplitOverlap { get; set; } = 0.25f;

    private ILogger? _log;
    protected ILogger Log => _log ??= Services.LogFor(GetType());
    protected bool DebugMode => Constants.DebugMode.Transcription;
    protected ILogger? DebugLog => DebugMode ? Log : null;

    protected IServiceProvider Services { get; }

    public TranscriptSplitter(IServiceProvider services)
        => Services = services;

    public async IAsyncEnumerable<TranscriptSegment> GetSegments(
        OpenAudioSegment audioSegment,
        IAsyncEnumerable<Transcript> transcripts,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        TranscriptSegment? segment = null;
        try {
            var lastSentTranscript = (Transcript?) null;
            await foreach (var transcript in transcripts.WithCancellation(cancellationToken).ConfigureAwait(false)) {
                if (lastSentTranscript == null) {
                    var (initialPrefix, initialSuffix) = transcript.Split(0);
                    segment = new TranscriptSegment(audioSegment, initialPrefix, 0);
                    DebugLog?.LogDebug("Start: {Start}", initialSuffix);
                    segment.Suffixes.Writer.TryWrite(initialSuffix);
                    yield return segment;
                    lastSentTranscript = transcript;
                    continue;
                }

                if (transcript.TextRange.End <= lastSentTranscript.TextRange.End || !lastSentTranscript.IsStable) {
                    // transcript is shorter than lastSentTranscript
                    var diff = transcript.DiffWith(segment!.Prefix);
                    DebugLog?.LogDebug("Append shorter: {Diff}", diff);
                    segment.Suffixes.Writer.TryWrite(diff);
                    lastSentTranscript = transcript;
                    continue;
                }

                var suffixStart = lastSentTranscript.TextRange.End;
                var suffix = transcript.GetSuffix(suffixStart);
                var contentStart = suffix.GetContentStart();
                if (contentStart == suffix.TextRange.End)
                    continue; // Empty suffix
                var contentStartTime = suffix.TextToTimeMap.Map(contentStart);
                DebugLog?.LogDebug(".. {Suffix}, content @ {ContentStartTime}", suffix, contentStartTime);

                var pauseDuration = contentStartTime - lastSentTranscript.GetContentEndTime();
                if (pauseDuration < SplitPauseDuration) {
                    var diff = transcript.DiffWith(segment!.Prefix);
                    DebugLog?.LogDebug("Append: {Diff}", diff);
                    segment.Suffixes.Writer.TryWrite(diff);
                    lastSentTranscript = transcript;
                    continue;
                }

                segment!.Suffixes.Writer.Complete();
                var (prefix, suffix1) = transcript.Split(contentStart, SplitOverlap);
                segment = segment.Next(prefix);
                DebugLog?.LogDebug("Split: {Start}", suffix1);
                segment.Suffixes.Writer.TryWrite(suffix1);
                lastSentTranscript = prefix;
                yield return segment;
            }
        }
        finally {
            // The error will be thrown anyway, but the last produced segment must be completed
            segment?.Suffixes.Writer.TryComplete();
        }
    }
}
