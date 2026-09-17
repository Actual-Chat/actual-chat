using System.Numerics;
using ActualChat.Diagnostics;
using ActualChat.Transcription;

namespace ActualChat.Chat;

public delegate Task<string> TranslateClause(
    string clause,
    TranslationResult[] context,
    CancellationToken cancellationToken);

/// <summary>
/// Translates a live transcript one source clause at a time, speculatively: a clause is sent to the
/// translator the moment it's complete in the transcript's tail, and its translation is promoted
/// as stable once the clause stabilizes unchanged - so the translator's latency is paid inside the
/// stability wait, not after it. A clause that stabilizes changed is translated again; everything
/// speculated after a changed clause is dropped, because it was translated against the old one.
/// </summary>
public sealed class ClauseTranslator(TranslateClause translate, ILogger log)
{
    private readonly Lock _lock = new();
    private readonly List<Speculation> _speculations = [];
    private Channel<Transcript>? _output;
    // The longest consistent source text: a stable item is a prefix of the unstable one before it,
    // so it only refreshes the stable length and map unless it contradicts or outgrows that text
    private string _text = "";
    private LinearMap _map = LinearMap.Zero;
    private int _stableLength;
    private LinearMap _stableMap = LinearMap.Zero;
    private bool _isLastStable;
    private bool _isEnd;
    // Where the transcriber last detected the end of an utterance segment: the text up to there
    // ends in a clause whatever its punctuation, as the whole text does once the source ends
    private int _segmentEnd;
    private int _promotedEnd;
    private Transcript _promoted = Transcript.Empty;
    private Transcript? _lastPromoted;
    private Transcript? _lastOutput;
    private Task _laneTask = Task.CompletedTask;

    private TranslateClause Translate { get; } = translate;
    private ILogger Log { get; } = log;

    public int ClauseCount { get; private set; }
    public int RetranslatedCount { get; private set; }
    public int DroppedCount { get; private set; }
    public LatencyStats CallLatency { get; } = new();

    public async IAsyncEnumerable<Transcript> Run(
        IAsyncEnumerable<Transcript> source,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        var output = Channel.CreateUnbounded<Transcript>(new UnboundedChannelOptions { SingleReader = true });
        _output = output;
        var feedTask = Feed(source, output.Writer, cancellationToken);
        await foreach (var transcript in output.Reader.ReadAllAsync(cancellationToken).ConfigureAwait(false))
            yield return transcript;
        await feedTask.ConfigureAwait(false);
    }

    // Private methods

    private async Task Feed(
        IAsyncEnumerable<Transcript> source,
        ChannelWriter<Transcript> writer,
        CancellationToken cancellationToken)
    {
        try {
            await foreach (var transcript in source.WithCancellation(cancellationToken).ConfigureAwait(false)) {
                lock (_lock) {
                    _isLastStable = transcript.IsStable;
                    if (transcript.IsSegmentEnd)
                        _segmentEnd = transcript.Text.Length;
                    if (transcript.IsStable) {
                        _stableLength = transcript.Text.Length;
                        _stableMap = transcript.TimeMap;
                        if (!_text.StartsWith(transcript.Text)) {
                            _text = transcript.Text;
                            _map = transcript.TimeMap;
                        }
                    }
                    else {
                        _text = transcript.Text;
                        _map = transcript.TimeMap;
                    }
                    Reconcile(cancellationToken);
                }
            }
            Task laneTask;
            lock (_lock) {
                // Nothing can revise the text any more, so the unstable tail is as final as the
                // stable text: its remainder is translated and promoted as the last clause - unless
                // the source ended on a stable item, which is its final word on the tail it left out
                _isEnd = true;
                if (_isLastStable) {
                    _text = _text[.._stableLength];
                    _map = _stableMap;
                }
                _stableLength = _text.Length;
                _stableMap = _map;
                Reconcile(cancellationToken);
                laneTask = _laneTask;
            }
            await laneTask.ConfigureAwait(false);
            writer.TryComplete();
        }
        catch (Exception e) {
            writer.TryComplete(e);
        }
    }

    private void Reconcile(CancellationToken cancellationToken)
    {
        // Under _lock. Lines the speculations up with the clauses of the current source text, starts
        // the missing ones, promotes the stable ones, and publishes whatever changed.
        var text = _text;
        var ends = SplitClauses(text);
        var start = _promotedEnd;
        for (var i = 0; i < ends.Count; i++) {
            var clause = text[start..ends[i]];
            var endTime = MapEnd(ends[i]);
            if (i < _speculations.Count && _speculations[i].Clause == clause) {
                // The stable time map is the authoritative one, so the end time follows the source
                _speculations[i].EndTime = endTime;
                start = ends[i];
                continue;
            }
            // A changed clause: what followed it was translated against the old one
            if (i < _speculations.Count)
                Drop(i);
            var speculation = new Speculation(clause, ends[i], endTime, cancellationToken);
            _speculations.Add(speculation);
            StartTranslation(speculation, cancellationToken);
            start = ends[i];
        }
        // Speculations past the last clause the current text has (a boundary vanished)
        if (_speculations.Count > ends.Count)
            Drop(ends.Count);
        Promote();
        Publish();
    }

    private List<int> SplitClauses(string text)
    {
        // Under _lock. A segment end still ahead of the promoted text cuts it like the source end
        // does: the remainder before it is a clause, and the clauses after it start from it
        var segmentEnd = Math.Min(_segmentEnd, text.Length);
        if (segmentEnd <= _promotedEnd)
            return ClauseSplitter.Split(text, _promotedEnd, _isEnd);

        var ends = ClauseSplitter.Split(text[..segmentEnd], _promotedEnd, true);
        ends.AddRange(ClauseSplitter.Split(text, segmentEnd, _isEnd));
        return ends;
    }

    private void Drop(int from)
    {
        // Under _lock. RetranslatedCount counts the completed translations a revision throws away,
        // DroppedCount the calls it cancels in flight
        for (var j = _speculations.Count - 1; j >= from; j--) {
            var speculation = _speculations[j];
            if (speculation.Translated != null)
                RetranslatedCount++;
            else
                DroppedCount++;
            speculation.Cancel();
        }
        _speculations.RemoveRange(from, _speculations.Count - from);
    }

    private void StartTranslation(Speculation speculation, CancellationToken cancellationToken)
    {
        // Under _lock
        var previousLaneTask = _laneTask;
        _laneTask = TranslateOnLane();
        return;

        async Task TranslateOnLane()
        {
            await previousLaneTask.SilentAwait(false);
            // The lane must never run inside the caller's lock: a lane whose predecessor is already
            // complete would otherwise continue synchronously right here, into Reconcile
            await Task.CompletedTask.ConfigureAwait(ConfigureAwaitOptions.ForceYielding);
            string translated;
            try {
                TranslationResult[] context;
                CancellationToken speculationToken;
                lock (_lock) {
                    var index = _speculations.IndexOf(speculation);
                    if (index < 0)
                        return;

                    speculationToken = speculation.CancellationToken;
                    var contextText = _text[.._promotedEnd];
                    var contextTranslated = _promoted.Text;
                    for (var i = 0; i < index; i++) {
                        contextText += _speculations[i].Clause;
                        contextTranslated += ToSuffix(contextTranslated, _speculations[i].Translated!);
                    }
                    context = contextText.IsNullOrWhiteSpace()
                        ? []
                        : [new TranslationResult(contextText, contextTranslated)];
                }
                var startedAt = CpuTimestamp.Now;
                translated = await Translate(speculation.Clause, context, speculationToken).ConfigureAwait(false);
                CallLatency.Add(startedAt.Elapsed);
                // An empty translation would leave the clause's end time out of the time map
                if (translated.IsNullOrWhiteSpace())
                    translated = speculation.Clause;
            }
            catch (Exception) when (speculation.IsCancelled) {
                // The speculation's token is linked to the run's, so this covers a cancelled run too
                return;
            }
            catch (Exception e) {
                // Whatever failed, the clause must land: Promote waits for its translation forever otherwise
                Log.LogWarning(e, "Clause translation failed, passing it through: {Clause}", speculation.Clause);
                translated = speculation.Clause;
            }
            lock (_lock) {
                if (speculation.IsCancelled)
                    return;

                speculation.Translated = translated;
                Promote();
                Publish();
            }
        }
    }

    private void Promote()
    {
        // Under _lock. The leading speculations whose clause is stable and translated become the promoted text
        while (_speculations.Count > 0) {
            var first = _speculations[0];
            if (!first.IsPromotable(_stableLength) || first.Translated == null)
                return;

            var suffix = ToSuffix(_promoted.Text, first.Translated);
            _promoted = _promoted.WithSuffix(suffix, first.EndTime) with { IsStable = true };
            _promotedEnd = first.End;
            _speculations.RemoveAt(0);
            first.Dispose();
            ClauseCount++;
        }
        // A whitespace-only remainder is no clause, but the dub reads "translated" only once the
        // translated end time reaches the source's, so the last clause's end time takes it over
        if (_isEnd && _speculations.Count == 0 && _promotedEnd < _text.Length && _promoted.Text.Length > 0) {
            var end = new Vector2(_promoted.Text.Length, MapEnd(_text.Length));
            _promoted = _promoted with {
                TimeMap = _promoted.TimeMap.AppendOrUpdateSuffix(end, Transcript.TimeMapEpsilon.X),
            };
            _promotedEnd = _text.Length;
        }
    }

    private void Publish()
    {
        // Under _lock. Stable first, then the speculative tail - the reader folds them in order. The
        // stable one goes out only when a promotion changed it: re-sending it after every landed
        // speculation would rewind the readers' text just to re-append the tail.
        if (!ReferenceEquals(_promoted, _lastPromoted)) {
            Write(_promoted);
            _lastPromoted = _promoted;
        }
        var tail = _promoted;
        foreach (var speculation in _speculations) {
            if (speculation.Translated == null)
                break;

            var suffix = ToSuffix(tail.Text, speculation.Translated);
            tail = tail.WithSuffix(suffix, speculation.EndTime) with { IsStable = false };
        }
        if (!ReferenceEquals(tail, _promoted))
            Write(tail);
    }

    private void Write(Transcript transcript)
    {
        // Under _lock. An end time refresh alone isn't worth a write unless it moves the end past
        // the epsilon the readers compare ends with
        if (_lastOutput is { } last
            && last.IsStable == transcript.IsStable
            && last.Text == transcript.Text
            && Math.Abs(last.TimeRange.End - transcript.TimeRange.End) <= Transcript.TimeMapEpsilon.Y)
            return;
        if (transcript.Text.Length == 0)
            return;

        _lastOutput = transcript;
        _output!.Writer.TryWrite(transcript);
    }

    private float MapEnd(int end)
        => end <= _stableLength ? _stableMap.Map(end) : _map.Map(end);

    // A clause's translation loses the space that separated the clause from the one before it
    private static string ToSuffix(string text, string translated)
        => text.Length > 0 && !translated.StartsWith(' ') ? $" {translated}" : translated;

    // Nested types

    private sealed class Speculation(string clause, int end, float endTime, CancellationToken cancellationToken)
        : IDisposable
    {
        private readonly CancellationTokenSource _cts
            = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

        public string Clause { get; } = clause;
        public int End { get; } = end;
        public float EndTime { get; set; } = endTime;
        public string? Translated { get; set; }
        public CancellationToken CancellationToken => _cts.Token;
        public bool IsCancelled => _cts.IsCancellationRequested;

        public void Dispose()
            => _cts.Dispose();

        public bool IsPromotable(int stableLength)
            => End <= stableLength;

        public void Cancel()
        {
            _cts.Cancel();
            _cts.Dispose();
        }
    }
}
