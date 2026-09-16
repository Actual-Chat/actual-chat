using ActualChat.Diagnostics;
using ActualChat.Streaming.Diagnostics;
using ActualChat.Transcription;

namespace ActualChat.Streaming;

/// <summary>
/// How far behind the speech the live transcript runs, per utterance: every transcript's
/// TimeRange.End is speech time, so its lag is now - (recordedAt + end).
/// </summary>
public sealed class TranscriptLatencyTrace(string streamId, Moment recordedAt, MomentClock clock)
{
    public LatencyStats Text { get; } = new();
    public LatencyStats Stable { get; } = new();
    public TimeSpan? FirstTextDelay { get; private set; }
    public float FirstTextSourceEnd { get; private set; }

    public void OnTranscript(Transcript transcript)
    {
        if (transcript.Text.IsNullOrWhiteSpace())
            return;

        var now = clock.Now;
        var sourceEnd = transcript.TimeRange.End;
        var lag = now - (recordedAt + TimeSpan.FromSeconds(sourceEnd));
        if (FirstTextDelay == null) {
            FirstTextDelay = now - recordedAt;
            FirstTextSourceEnd = sourceEnd;
        }
        if (transcript.IsStable)
            Stable.Add(lag);
        else
            Text.Add(lag);
    }

    public void Report(ILogger log)
    {
        if (FirstTextDelay == null)
            return;

        log.LogInformation("{Trace}", ToString());
        foreach (var lag in Text.Values)
            StreamingMeters.TranscriptLag.Record(lag.TotalSeconds, new KeyValuePair<string, object?>("kind", "text"));
        foreach (var lag in Stable.Values)
            StreamingMeters.TranscriptLag.Record(lag.TotalSeconds, new KeyValuePair<string, object?>("kind", "stable"));
    }

    public override string ToString()
        => $"Transcript latency #{streamId}: first text +{FirstTextDelay?.TotalSeconds ?? 0:F1}s "
            + $"at {FirstTextSourceEnd:F1}s of speech; text lag {Text}; stable lag {Stable}";
}
