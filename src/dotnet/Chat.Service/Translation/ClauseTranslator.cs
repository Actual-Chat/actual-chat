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
    private Transcript _source = Transcript.Empty;
    private int _stableLength;
    private bool _isEnd;
    private int _promotedEnd;
    private Transcript _promoted = Transcript.Empty;
    private Transcript? _lastPromoted;
    private Transcript? _lastOutput;
    private Task _laneTask = Task.CompletedTask;

    private TranslateClause Translate { get; } = translate;
    private ILogger Log { get; } = log;

    public int ClauseCount { get; private set; }
    public int RetranslatedCount { get; private set; }

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
                    _source = transcript;
                    if (transcript.IsStable)
                        _stableLength = transcript.Text.Length;
                    Reconcile(cancellationToken);
                }
            }
            Task laneTask;
            lock (_lock) {
                // Nothing can revise the text any more, so the unstable tail is as final as the
                // stable text: its remainder is translated and promoted as the last clause
                _isEnd = true;
                _stableLength = _source.Text.Length;
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
        var text = _source.Text;
        var ends = ClauseSplitter.Split(text, _promotedEnd, _isEnd);
        var start = _promotedEnd;
        for (var i = 0; i < ends.Count; i++) {
            var clause = text[start..ends[i]];
            var endTime = _source.TimeMap.Map(ends[i]);
            if (i < _speculations.Count && _speculations[i].Clause == clause) {
                // The stable time map is the authoritative one, so the end time follows the source
                _speculations[i].EndTime = endTime;
                start = ends[i];
                continue;
            }
            if (i < _speculations.Count) {
                // A changed clause: what followed it was translated against the old one
                if (_speculations[i].Translated != null)
                    RetranslatedCount++;
                for (var j = _speculations.Count - 1; j >= i; j--)
                    _speculations[j].Cancel();
                _speculations.RemoveRange(i, _speculations.Count - i);
            }
            var speculation = new Speculation(clause, ends[i], endTime, cancellationToken);
            _speculations.Add(speculation);
            StartTranslation(speculation, cancellationToken);
            start = ends[i];
        }
        // Speculations past the last clause the current text has (a boundary vanished)
        if (_speculations.Count > ends.Count) {
            for (var j = _speculations.Count - 1; j >= ends.Count; j--)
                _speculations[j].Cancel();
            _speculations.RemoveRange(ends.Count, _speculations.Count - ends.Count);
        }
        Promote();
        Publish();
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
            if (speculation.IsCancelled)
                return;

            TranslationResult[] context;
            CancellationToken speculationToken;
            lock (_lock) {
                var index = _speculations.IndexOf(speculation);
                if (index < 0)
                    return;

                speculationToken = speculation.CancellationToken;
                var contextText = _promoted.Text;
                var contextTranslated = _promoted.Text;
                for (var i = 0; i < index; i++) {
                    contextText += _speculations[i].Clause;
                    contextTranslated += ToSuffix(contextTranslated, _speculations[i].Translated!);
                }
                context = contextText.IsNullOrWhiteSpace()
                    ? []
                    : [new TranslationResult(contextText, contextTranslated)];
            }
            string translated;
            try {
                translated = await Translate(speculation.Clause, context, speculationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (speculation.IsCancelled) {
                return;
            }
            catch (Exception e) when (!cancellationToken.IsCancellationRequested) {
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
        // Under _lock
        if (_lastOutput is { } last && last.IsStable == transcript.IsStable && last.Text == transcript.Text)
            return;
        if (transcript.Text.Length == 0)
            return;

        _lastOutput = transcript;
        _output!.Writer.TryWrite(transcript);
    }

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
