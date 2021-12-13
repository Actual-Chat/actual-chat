using ActualChat.Transcription;

namespace ActualChat.Audio.Processing;

public class TranscriptSplitter
{
    public static TimeSpan SplitPauseDuration { get; set; } = TimeSpan.FromSeconds(1);
    public static TimeSpan SplitOverlap { get; set; } = TimeSpan.FromSeconds(0.25);

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

                if (transcript.TextRange.End <= lastSentTranscript.TextRange.End) {
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
                var pauseDuration = contentStartTime - lastSentTranscript.TimeRange.End;
                DebugLog?.LogDebug(
                    ".. {Suffix}, content @ {ContentStart} -> {ContentStartTime}",
                    suffix, contentStart, contentStartTime);

                if (pauseDuration < SplitPauseDuration.TotalSeconds) {
                    var diff = transcript.DiffWith(segment!.Prefix);
                    DebugLog?.LogDebug("Append: {Diff}", diff);
                    segment.Suffixes.Writer.TryWrite(diff);
                    lastSentTranscript = transcript;
                    continue;
                }

                segment!.Suffixes.Writer.Complete();
                var (prefix, suffix1) = transcript.Split(contentStart, SplitOverlap.TotalSeconds);
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
