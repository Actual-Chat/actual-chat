using ActualChat.Chat;
using ActualChat.Transcription;

namespace ActualChat.Audio.Processing;

public class TranscriptSplitter : TranscriptionProcessorBase
{
    public static TimeSpan TextEntryWaitDelay { get; set; } = TimeSpan.FromSeconds(0.2);
    public static float QuickSplitPauseDuration { get; set; } = 0.1f;
    public static float SplitPauseDuration { get; set; } = 0.75f;
    public static float SplitOverlap { get; set; } = 0.25f;

    protected IChatsBackend ChatsBackend { get; }

    public TranscriptSplitter(IServiceProvider services) : base(services)
        => ChatsBackend = services.GetRequiredService<IChatsBackend>();

    public async IAsyncEnumerable<TranscriptSegment> GetSegments(
        OpenAudioSegment audioSegment,
        IAsyncEnumerable<Transcript> transcripts,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var cpuClock = Clocks.CpuClock;
        var chatId = audioSegment.AudioRecord.ChatId;

        var segmentTextId = 0L;
        TranscriptSegment? segment = null;
        try {
            var lastSentTranscript = (Transcript?)null;
            await foreach (var transcript in transcripts.WithCancellation(cancellationToken).ConfigureAwait(false)) {
                if (lastSentTranscript == null) {
                    var (initialPrefix, initialSuffix) = transcript.Split(0);
                    segment = new TranscriptSegment(audioSegment, initialPrefix, 0);
                    DebugLog?.LogDebug("Start: {Start}", initialSuffix);
                    segment.Suffixes.Writer.TryWrite(initialSuffix);
                    yield return segment;
                    lastSentTranscript = transcript;
                    segmentTextId = await GetMaxTextId(true).ConfigureAwait(false);
                    continue;
                }

                if (transcript.TextRange.End <= lastSentTranscript.TextRange.End || !lastSentTranscript.IsStable) {
                    // transcript is shorter than lastSentTranscript
                    var suffix = transcript.GetSuffix(segment!.Prefix.TextRange.End);
                    DebugLog?.LogDebug("| {Suffix} (unstable)", suffix);
                    segment.Suffixes.Writer.TryWrite(suffix);
                    lastSentTranscript = transcript;
                }
                else {
                    var suffix = transcript.GetSuffix(lastSentTranscript.TextRange.End);
                    var contentStart = suffix.GetContentStart();
                    if (contentStart == suffix.TextRange.End)
                        continue; // Empty suffix
                    var contentStartTime = suffix.TextToTimeMap.Map(contentStart);

                    var pauseDuration = contentStartTime - lastSentTranscript.GetContentEndTime();
                    var maxTextId = await GetMaxTextId(false).ConfigureAwait(false);
                    var splitPauseDuration = maxTextId == segmentTextId
                        ? SplitPauseDuration
                        : QuickSplitPauseDuration;
                    if (pauseDuration < splitPauseDuration) {
                        DebugLog?.LogDebug("| {Suffix} ({PauseDuration}s pause)", suffix, pauseDuration);
                        segment!.Suffixes.Writer.TryWrite(transcript);
                        lastSentTranscript = transcript;
                        continue;
                    }

                    var (prefix, suffix1) = transcript.Split(contentStart, SplitOverlap);
                    segment!.Suffixes.Writer.Complete();
                    lastSentTranscript = prefix;
                    DebugLog?.LogDebug("⏎ {Suffix} ({PauseDuration}s pause)", suffix1, pauseDuration);

                    segment = segment.Next(prefix);
                    segment.Suffixes.Writer.TryWrite(suffix1);
                    yield return segment;
                    segmentTextId = await GetMaxTextId(true).ConfigureAwait(false);
                }
            }
        }
        finally {
            // The error will be thrown anyway, but the last produced segment must be completed
            segment?.Suffixes.Writer.TryComplete();
        }

        async Task<long> GetMaxTextId(bool delay)
        {
            if (delay)
                await cpuClock.Delay(TextEntryWaitDelay, cancellationToken).ConfigureAwait(false);
            var idRange = await ChatsBackend.GetIdRange(chatId, ChatEntryType.Text, true, cancellationToken).ConfigureAwait(false);
            return idRange.End;
        }
    }
}
