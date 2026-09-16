using ActualChat.Diagnostics;
using ActualChat.Streaming.Diagnostics;
using ActualChat.Transcription;

namespace ActualChat.Streaming;

/// <summary>
/// How far behind the speech each stage of one live dub runs: translated and spoken lags are
/// now - (recordedAt + TimeRange.End) of the text, the TTS interval is first text to first audio
/// per stream, and the first word is the first stream's first audio behind the first chunk's speech.
/// </summary>
public sealed class DubLatencyTrace(StreamId dubStreamId, Moment recordedAt, MomentClock clock)
    : ISpeechSynthesisListener
{
    private Moment _requestedAt;
    private Moment? _streamOpenedAt;
    // Written once by the listener thread, read once by the worker thread: a stale read costs
    // at worst a missing first-word number, never a crash.
    private float? _firstSpokenSourceEnd;

    public LatencyStats Translated { get; } = new();
    public LatencyStats Spoken { get; } = new();
    public LatencyStats TtsFirstAudio { get; } = new();
    public TimeSpan? DecisionDelay { get; private set; }
    public TimeSpan? FirstWordLag { get; private set; }

    public void OnRequested()
        => _requestedAt = clock.Now;

    public void OnDecided()
        => DecisionDelay ??= clock.Now - _requestedAt;

    public void OnTranslated(Transcript translated)
    {
        if (translated.IsStable)
            Translated.Add(LagOf(translated));
    }

    public void OnSpoken(Transcript translated)
    {
        Spoken.Add(LagOf(translated));
        _firstSpokenSourceEnd ??= translated.TimeRange.End;
    }

    void ISpeechSynthesisListener.OnStreamOpened()
        => _streamOpenedAt = clock.Now;

    void ISpeechSynthesisListener.OnAudioStarted()
    {
        var now = clock.Now;
        if (_streamOpenedAt is { } openedAt) {
            TtsFirstAudio.Add(now - openedAt);
            _streamOpenedAt = null;
        }
        if (FirstWordLag == null && _firstSpokenSourceEnd is { } sourceEnd)
            FirstWordLag = now - (recordedAt + TimeSpan.FromSeconds(sourceEnd));
    }

    public void Report(ILogger log)
    {
        if (DecisionDelay == null)
            return;

        log.LogInformation("{Trace}", ToString());
        StreamingMeters.DubDecisionDelay.Record(DecisionDelay.Value.TotalSeconds);
        foreach (var lag in Translated.Values)
            StreamingMeters.DubLag.Record(lag.TotalSeconds, Stage("translated"));
        foreach (var lag in Spoken.Values)
            StreamingMeters.DubLag.Record(lag.TotalSeconds, Stage("spoken"));
        foreach (var interval in TtsFirstAudio.Values)
            StreamingMeters.DubTtsFirstAudio.Record(interval.TotalSeconds);
        if (FirstWordLag is { } firstWord)
            StreamingMeters.DubLag.Record(firstWord.TotalSeconds, Stage("first_word"));
    }

    public override string ToString()
    {
        var firstWord = FirstWordLag is { } lag ? $"{lag.TotalSeconds:F1}s behind speech" : "-";
        return $"Dub latency #{dubStreamId}: decided +{DecisionDelay?.TotalSeconds ?? 0:F1}s; "
            + $"translated lag {Translated}; spoken lag {Spoken}; tts first audio {TtsFirstAudio}; "
            + $"first word {firstWord}";
    }

    // Private methods

    private TimeSpan LagOf(Transcript transcript)
        => clock.Now - (recordedAt + TimeSpan.FromSeconds(transcript.TimeRange.End));

    private static KeyValuePair<string, object?> Stage(string stage)
        => new("stage", stage);
}
