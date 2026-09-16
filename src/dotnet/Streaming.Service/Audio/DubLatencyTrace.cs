using ActualChat.Diagnostics;
using ActualChat.Streaming.Diagnostics;
using ActualChat.Transcription;

namespace ActualChat.Streaming;

/// <summary>
/// How far behind the speech each stage of one live dub runs: translated and spoken lags are
/// now - (recordedAt + TimeRange.End) of the text, the TTS open delay is first spoken chunk to
/// first text sent, the TTS interval is first text to first audio per stream (a stream killed
/// before audio folds into its replacement's sample), and the first word is the first stream's
/// first audio behind the first chunk's speech.
/// </summary>
public sealed class DubLatencyTrace(StreamId dubStreamId, Moment recordedAt, MomentClock clock)
    : ISpeechSynthesisListener
{
    private Moment _requestedAt;
    private float? _requestedSourceEnd;
    private Moment? _streamOpenedAt;
    // Set before the chunk is written to the TTS channel, so the channel hand-off publishes it to
    // the TTS threads before any audio can come back.
    private Moment? _firstSpokenAt;
    private float? _firstSpokenSourceEnd;
    private bool _isDub;

    public LatencyStats Translated { get; } = new();
    public LatencyStats Spoken { get; } = new();
    public LatencyStats TtsFirstAudio { get; } = new();
    public TimeSpan? DecisionDelay { get; private set; }
    public TimeSpan? TtsOpenDelay { get; private set; }
    public TimeSpan? FirstWordLag { get; private set; }

    public void OnRequested()
        => _requestedAt = clock.Now;

    public void OnSourceReady(float sourceEnd)
        => _requestedSourceEnd ??= sourceEnd;

    public void OnDecided(bool isDub)
    {
        DecisionDelay ??= clock.Now - _requestedAt;
        _isDub = isDub;
    }

    public void OnTranslated(Transcript translated)
    {
        if (translated.IsStable)
            Translated.Add(LagOf(translated));
    }

    public void OnSpoken(Transcript translated)
    {
        Spoken.Add(LagOf(translated));
        _firstSpokenSourceEnd ??= translated.TimeRange.End;
        _firstSpokenAt ??= clock.Now;
    }

    void ISpeechSynthesisListener.OnStreamOpened()
    {
        var now = clock.Now;
        // A stream killed before any audio has no replacement time of its own yet, so its open
        // time survives here for the replacement's tts-first-audio sample to be measured from.
        _streamOpenedAt ??= now;
        if (_firstSpokenAt is { } firstSpokenAt)
            TtsOpenDelay ??= now - firstSpokenAt;
    }

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

        StreamingMeters.DubDecisionDelay.Record(DecisionDelay.Value.TotalSeconds);
        if (!_isDub)
            return;

        log.LogInformation("{Trace}", ToString());
        foreach (var lag in Translated.Values)
            StreamingMeters.DubLag.Record(lag.TotalSeconds, Stage("translated"));
        foreach (var lag in Spoken.Values)
            StreamingMeters.DubLag.Record(lag.TotalSeconds, Stage("spoken"));
        foreach (var interval in TtsFirstAudio.Values)
            StreamingMeters.DubTtsFirstAudio.Record(interval.TotalSeconds);
        if (TtsOpenDelay is { } openDelay)
            StreamingMeters.DubTtsOpenDelay.Record(openDelay.TotalSeconds);
        if (FirstWordLag is { } firstWord)
            StreamingMeters.DubLag.Record(firstWord.TotalSeconds, Stage("first_word"));
    }

    public override string ToString()
    {
        var requestedAt = _requestedSourceEnd is { } sourceEnd ? $"{sourceEnd:F1}s of speech" : "-";
        var ttsOpened = TtsOpenDelay is { } openDelay ? $"+{openDelay.TotalSeconds:F1}s after the first chunk" : "-";
        var firstWord = FirstWordLag is { } lag ? $"{lag.TotalSeconds:F1}s behind speech" : "-";
        return $"Dub latency #{dubStreamId}: decided +{DecisionDelay?.TotalSeconds ?? 0:F1}s; "
            + $"requested at {requestedAt}; translated lag {Translated}; spoken lag {Spoken}; "
            + $"tts opened {ttsOpened}; tts first audio {TtsFirstAudio}; first word {firstWord}";
    }

    // Private methods

    private TimeSpan LagOf(Transcript transcript)
        => clock.Now - (recordedAt + TimeSpan.FromSeconds(transcript.TimeRange.End));

    private static KeyValuePair<string, object?> Stage(string stage)
        => new("stage", stage);
}
