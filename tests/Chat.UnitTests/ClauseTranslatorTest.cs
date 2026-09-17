using System.Numerics;
using ActualChat.Transcription;

namespace ActualChat.Chat.UnitTests;

public class ClauseTranslatorTest
{
    [Fact]
    public async Task AClauseIsTranslatedBeforeItIsStableAndPromotedWithoutASecondCall()
    {
        var fake = new FakeTranslate();
        var source = Channel.CreateUnbounded<Transcript>();
        var translator = new ClauseTranslator(fake.Translate, NullLogger.Instance);
        var outputs = new List<Transcript>();
        var runTask = Collect(translator.Run(source.Reader.ReadAllAsync(), CancellationToken.None), outputs);

        source.Writer.TryWrite(Unstable("Привет, как дела? Я", 3f));
        await fake.WhenCalled(1);
        fake.Calls.Should().Equal(["Привет, как дела?"], "the complete clause is translated while it's still unstable");
        fake.Respond("Привет, как дела?");
        await WhenCount(outputs, 1);
        outputs[0].IsStable.Should().BeFalse();
        outputs[0].Text.Should().Be("EN[Привет, как дела?]");

        source.Writer.TryWrite(Stable("Привет, как дела?", 2f));
        await WhenCount(outputs, 2);
        outputs[1].IsStable.Should().BeTrue();
        outputs[1].Text.Should().Be("EN[Привет, как дела?]");
        outputs[1].TimeRange.End.Should().BeApproximately(2f, 0.01f, "the clause's end time is the source's");
        fake.Calls.Should().HaveCount(1, "the stable clause equals the speculation, so it isn't translated again");

        source.Writer.Complete();
        await runTask;
        translator.ClauseCount.Should().Be(1);
        translator.RetranslatedCount.Should().Be(0);
    }

    [Fact]
    public async Task AStablePrefixInsideASpeculatedClauseKeepsTheSpeculation()
    {
        var fake = new FakeTranslate();
        var source = Channel.CreateUnbounded<Transcript>();
        var translator = new ClauseTranslator(fake.Translate, NullLogger.Instance);
        var outputs = new List<Transcript>();
        var runTask = Collect(translator.Run(source.Reader.ReadAllAsync(), CancellationToken.None), outputs);

        source.Writer.TryWrite(Unstable("Привет, как дела? Я", 3f));
        await fake.WhenCalled(1);

        // Soniox finalizes a few tokens at a time: the stable text stops inside the speculated clause
        source.Writer.TryWrite(Stable("Привет, как", 1.5f));
        await Task.Delay(100);
        fake.IsPendingCancelled.Should().BeFalse("a stable prefix that agrees with the tail changes nothing");
        fake.Calls.Should().HaveCount(1, "the speculation is kept, not re-created");

        fake.Respond("Привет, как дела?");
        await WhenCount(outputs, 1);
        outputs[0].IsStable.Should().BeFalse();
        source.Writer.TryWrite(Stable("Привет, как дела?", 2f));
        await WhenCount(outputs, 2);
        outputs[1].IsStable.Should().BeTrue();
        outputs[1].Text.Should().Be("EN[Привет, как дела?]");
        outputs[1].TimeRange.End.Should().BeApproximately(2f, 0.01f, "the clause's end time is the stable map's");
        fake.Calls.Should().HaveCount(1, "the clause stabilized unchanged, so its speculation is promoted as is");

        source.Writer.Complete();
        await runTask;
        fake.Calls.Should().HaveCount(1, "the source ended on a stable item that left the tail out, so it's gone");
        translator.ClauseCount.Should().Be(1);
        translator.DroppedCount.Should().Be(0);
    }

    [Fact]
    public async Task AStablePrefixContradictingTheTailDropsTheSpeculation()
    {
        var fake = new FakeTranslate();
        var source = Channel.CreateUnbounded<Transcript>();
        var translator = new ClauseTranslator(fake.Translate, NullLogger.Instance);
        var outputs = new List<Transcript>();
        var runTask = Collect(translator.Run(source.Reader.ReadAllAsync(), CancellationToken.None), outputs);

        source.Writer.TryWrite(Unstable("Привет, как дела? Я", 3f));
        await fake.WhenCalled(1);

        // The finalized tokens differ from the tail they were speculated on
        source.Writer.TryWrite(Stable("Привет, кот", 1.5f));
        await fake.WhenPendingCancelled();
        fake.Calls.Should().HaveCount(1);

        source.Writer.TryWrite(Unstable("Привет, кот дела? Я", 3f));
        await fake.WhenCalled(2);
        fake.Calls[1].Should().Be("Привет, кот дела?", "the clause is speculated again once it's complete again");
        fake.Respond("Привет, кот дела?");
        await WhenCount(outputs, 1);
        outputs[0].IsStable.Should().BeFalse();

        source.Writer.TryWrite(Stable("Привет, кот дела?", 2f));
        await WhenCount(outputs, 2);
        outputs[1].IsStable.Should().BeTrue();
        outputs[1].Text.Should().Be("EN[Привет, кот дела?]");
        fake.Calls.Should().HaveCount(2);

        source.Writer.Complete();
        await runTask;
        translator.DroppedCount.Should().Be(1, "the first speculation was cancelled in flight");
        translator.RetranslatedCount.Should().Be(0, "it never completed, so nothing was thrown away");
    }

    [Fact]
    public async Task ARevisedClauseIsTranslatedAgainAndTheTextRewinds()
    {
        var fake = new FakeTranslate();
        var source = Channel.CreateUnbounded<Transcript>();
        var translator = new ClauseTranslator(fake.Translate, NullLogger.Instance);
        var outputs = new List<Transcript>();
        var runTask = Collect(translator.Run(source.Reader.ReadAllAsync(), CancellationToken.None), outputs);

        source.Writer.TryWrite(Unstable("Привет, как дела? Я", 3f));
        await fake.WhenCalled(1);
        fake.Respond("Привет, как дела?");
        await WhenCount(outputs, 1);

        // Soniox revised a word before the clause stabilized
        source.Writer.TryWrite(Stable("Привет, как дела!", 2f));
        await fake.WhenCalled(2);
        fake.Calls[1].Should().Be("Привет, как дела!");
        fake.Respond("Привет, как дела!");
        await WhenCount(outputs, 2);
        outputs[1].IsStable.Should().BeTrue();
        outputs[1].Text.Should().Be("EN[Привет, как дела!]");

        source.Writer.Complete();
        await runTask;
        translator.RetranslatedCount.Should().Be(1);
    }

    [Fact]
    public async Task LaterSpeculationsAreDroppedWhenAnEarlierClauseChanges()
    {
        var fake = new FakeTranslate();
        var source = Channel.CreateUnbounded<Transcript>();
        var translator = new ClauseTranslator(fake.Translate, NullLogger.Instance);
        var outputs = new List<Transcript>();
        var runTask = Collect(translator.Run(source.Reader.ReadAllAsync(), CancellationToken.None), outputs);

        source.Writer.TryWrite(Unstable("Первое. Второе. Тре", 3f));
        await fake.WhenCalled(1);
        fake.Respond("Первое.");
        await fake.WhenCalled(2);
        fake.Calls[1].Should().Be(" Второе.");

        // The boundary moved: the first clause is now longer
        source.Writer.TryWrite(Unstable("Первое второе. Третье. Че", 3f));
        await fake.WhenCalled(3);
        fake.Calls[2].Should().Be("Первое второе.", "everything after a changed clause is dropped and redone in order");
        fake.Respond("Первое второе.");
        await fake.WhenCalled(4);
        fake.Calls[3].Should().Be(" Третье.");
        fake.Respond(" Третье.");
        await WhenCount(outputs, 3);
        outputs[^1].Text.Should().Be("EN[Первое второе.] EN[ Третье.]");

        // The unstable tail at the end is translated as the last clause
        source.Writer.Complete();
        await fake.WhenCalled(5);
        fake.Calls[4].Should().Be(" Че");
        fake.Respond(" Че");
        await runTask;
        outputs[^1].IsStable.Should().BeTrue();
        outputs[^1].Text.Should().Be("EN[Первое второе.] EN[ Третье.] EN[ Че]");
    }

    [Fact]
    public async Task ContextIsThePromotedTextPlusEarlierSpeculations()
    {
        var fake = new FakeTranslate();
        var source = Channel.CreateUnbounded<Transcript>();
        var translator = new ClauseTranslator(fake.Translate, NullLogger.Instance);
        var outputs = new List<Transcript>();
        var runTask = Collect(translator.Run(source.Reader.ReadAllAsync(), CancellationToken.None), outputs);

        source.Writer.TryWrite(Unstable("Первое. Второе. Тре", 3f));
        await fake.WhenCalled(1);
        fake.Contexts[0].Should().BeEmpty();
        fake.Respond("Первое.");
        await fake.WhenCalled(2);
        fake.Contexts[1].Should().Equal([new TranslationResult("Первое.", "EN[Первое.]")]);
        fake.Respond(" Второе.");

        // The unstable tail at the end is translated as the last clause
        source.Writer.Complete();
        await fake.WhenCalled(3);
        fake.Calls[2].Should().Be(" Тре");
        fake.Respond(" Тре");
        await runTask;
        outputs[^1].IsStable.Should().BeTrue();
        outputs[^1].Text.Should().Be("EN[Первое.] EN[ Второе.] EN[ Тре]");
    }

    [Fact]
    public async Task TheRemainderIsTheLastClauseWhenTheSourceEnds()
    {
        var fake = new FakeTranslate();
        var source = Channel.CreateUnbounded<Transcript>();
        var translator = new ClauseTranslator(fake.Translate, NullLogger.Instance);
        var outputs = new List<Transcript>();
        var runTask = Collect(translator.Run(source.Reader.ReadAllAsync(), CancellationToken.None), outputs);

        source.Writer.TryWrite(Stable("Привет, как у тебя", 2f));
        await Task.Delay(100);
        fake.Calls.Should().BeEmpty("no boundary, no overflow: nothing to translate yet");

        source.Writer.Complete();
        await fake.WhenCalled(1);
        fake.Calls[0].Should().Be("Привет, как у тебя");
        fake.Respond("Привет, как у тебя");
        await runTask;
        outputs[^1].IsStable.Should().BeTrue();
        outputs[^1].Text.Should().Be("EN[Привет, как у тебя]");
        outputs[^1].TimeRange.End.Should().BeApproximately(2f, 0.01f);
    }

    [Fact]
    public async Task AnEndpointMakesTheRemainderAClause()
    {
        var fake = new FakeTranslate();
        var source = Channel.CreateUnbounded<Transcript>();
        var translator = new ClauseTranslator(fake.Translate, NullLogger.Instance);
        var outputs = new List<Transcript>();
        var runTask = Collect(translator.Run(source.Reader.ReadAllAsync(), CancellationToken.None), outputs);

        source.Writer.TryWrite(Unstable("Да", 1f));
        await Task.Delay(100);
        fake.Calls.Should().BeEmpty("no punctuation, no overflow: nothing to translate yet");

        source.Writer.TryWrite(Stable("Да", 1f) with { IsSegmentEnd = true });
        await fake.WhenCalled(1);
        fake.Calls[0].Should().Be("Да", "the transcriber heard the utterance out, so its remainder is a clause");
        fake.Respond("Да");
        await WhenCount(outputs, 1);
        outputs[0].IsStable.Should().BeTrue("the clause is stable, so it's promoted at once");
        outputs[0].Text.Should().Be("EN[Да]");
        outputs[0].TimeRange.End.Should().BeApproximately(1f, 0.01f);

        // The stream goes on: the next segment is split as usual, from the promoted end
        source.Writer.TryWrite(Unstable("Да Как дела? Я", 3f));
        await fake.WhenCalled(2);
        fake.Calls[1].Should().Be(" Как дела?", "a segment end doesn't end the stream");
        fake.Respond(" Как дела?");
        await WhenCount(outputs, 2);
        outputs[1].IsStable.Should().BeFalse();
        outputs[1].Text.Should().Be("EN[Да] EN[ Как дела?]");

        source.Writer.Complete();
        await fake.WhenCalled(3);
        fake.Calls[2].Should().Be(" Я");
        fake.Respond(" Я");
        await runTask;
        outputs[^1].IsStable.Should().BeTrue();
        outputs[^1].Text.Should().Be("EN[Да] EN[ Как дела?] EN[ Я]");
        translator.ClauseCount.Should().Be(3);
    }

    [Fact]
    public async Task AnUnstableTailAtTheEndIsTranslatedAsTheLastClause()
    {
        var fake = new FakeTranslate();
        var source = Channel.CreateUnbounded<Transcript>();
        var translator = new ClauseTranslator(fake.Translate, NullLogger.Instance);
        var outputs = new List<Transcript>();
        var runTask = Collect(translator.Run(source.Reader.ReadAllAsync(), CancellationToken.None), outputs);

        source.Writer.TryWrite(Unstable("Привет, как дела? Я иду", 3f));
        await fake.WhenCalled(1);
        fake.Respond("Привет, как дела?");
        await WhenCount(outputs, 1);

        source.Writer.Complete();
        await fake.WhenCalled(2);
        fake.Calls[1].Should().Be(" Я иду", "nothing can revise the tail any more, so it's the last clause");
        fake.Respond(" Я иду");
        await runTask;
        outputs[^1].IsStable.Should().BeTrue();
        outputs[^1].Text.Should().Be("EN[Привет, как дела?] EN[ Я иду]");
        outputs[^1].TimeRange.End.Should().BeApproximately(3f, 0.01f, "the tail's end time is the source's end");
    }

    [Fact]
    public async Task AWhitespaceRemainderAtTheEndExtendsTheLastClausesEndTime()
    {
        var fake = new FakeTranslate();
        var source = Channel.CreateUnbounded<Transcript>();
        var translator = new ClauseTranslator(fake.Translate, NullLogger.Instance);
        var outputs = new List<Transcript>();
        var runTask = Collect(translator.Run(source.Reader.ReadAllAsync(), CancellationToken.None), outputs);

        source.Writer.TryWrite(Stable("Привет. ", 2f));
        await fake.WhenCalled(1);
        fake.Respond("Привет.");
        await WhenCount(outputs, 1);
        outputs[0].TimeRange.End.Should().BeApproximately(1.75f, 0.01f, "the clause ends before the trailing space");

        source.Writer.Complete();
        await runTask;
        fake.Calls.Should().HaveCount(1, "whitespace is no clause");
        outputs[^1].Text.Should().Be("EN[Привет.]");
        outputs[^1].TimeRange.End.Should().BeApproximately(2f, 0.01f,
            "the dub reads the translation only once its end time reaches the source's");
    }

    [Fact]
    public async Task ASynchronousFaultInTheDelegatePassesTheClauseThrough()
    {
        var source = Channel.CreateUnbounded<Transcript>();
        var translator = new ClauseTranslator(
            (_, _, _) => throw new InvalidOperationException("boom"),
            NullLogger.Instance);
        var outputs = new List<Transcript>();
        var runTask = Collect(translator.Run(source.Reader.ReadAllAsync(), CancellationToken.None), outputs);

        source.Writer.TryWrite(Stable("Привет, как дела?", 2f));
        await WhenCount(outputs, 1);
        outputs[0].IsStable.Should().BeTrue("a fault outside the call itself must not leave the clause pending");
        outputs[0].Text.Should().Be("Привет, как дела?");

        source.Writer.Complete();
        await runTask;
    }

    [Fact]
    public async Task AFailedCallPassesTheClauseThroughVerbatim()
    {
        var fake = new FakeTranslate();
        var source = Channel.CreateUnbounded<Transcript>();
        var translator = new ClauseTranslator(fake.Translate, NullLogger.Instance);
        var outputs = new List<Transcript>();
        var runTask = Collect(translator.Run(source.Reader.ReadAllAsync(), CancellationToken.None), outputs);

        source.Writer.TryWrite(Stable("Привет, как дела?", 2f));
        await fake.WhenCalled(1);
        fake.Fail(new InvalidOperationException("boom"));
        await WhenCount(outputs, 1);
        outputs[0].IsStable.Should().BeTrue();
        outputs[0].Text.Should().Be("Привет, как дела?");

        source.Writer.Complete();
        await runTask;
    }

    [Fact]
    public async Task ClausesArePromotedInOrderEvenWhenALaterOneStabilizesFirst()
    {
        // The stable source can only grow as a prefix, so "later stabilizes first" means: the
        // second clause's translation lands before the first's - the output must still wait
        var fake = new FakeTranslate();
        var source = Channel.CreateUnbounded<Transcript>();
        var translator = new ClauseTranslator(fake.Translate, NullLogger.Instance);
        var outputs = new List<Transcript>();
        var runTask = Collect(translator.Run(source.Reader.ReadAllAsync(), CancellationToken.None), outputs);

        source.Writer.TryWrite(Stable("Первое. Второе.", 2f));
        await fake.WhenCalled(1);
        fake.Calls.Should().HaveCount(1, "the lane is sequential: the second clause waits for the first's context");
        fake.Respond("Первое.");
        await fake.WhenCalled(2);
        fake.Contexts[1].Should().Equal(
            [new TranslationResult("Первое.", "EN[Первое.]")],
            "the context pairs the promoted source text with its translation");
        fake.Respond(" Второе.");
        await WhenCount(outputs, 2);
        outputs[0].Text.Should().Be("EN[Первое.]");
        outputs[1].Text.Should().Be("EN[Первое.] EN[ Второе.]");
        outputs.Should().OnlyContain(t => t.IsStable);

        source.Writer.Complete();
        await runTask;
        translator.ClauseCount.Should().Be(2);
    }

    [Fact]
    public async Task AVanishedBoundaryDropsTheSpeculationsPastIt()
    {
        var fake = new FakeTranslate();
        var source = Channel.CreateUnbounded<Transcript>();
        var translator = new ClauseTranslator(fake.Translate, NullLogger.Instance);
        var outputs = new List<Transcript>();
        var runTask = Collect(translator.Run(source.Reader.ReadAllAsync(), CancellationToken.None), outputs);

        source.Writer.TryWrite(Unstable("Первое. Второе. Тре", 3f));
        await fake.WhenCalled(1);
        fake.Respond("Первое.");
        await fake.WhenCalled(2);
        await WhenCount(outputs, 1);

        // The second boundary vanished while its clause was in flight
        source.Writer.TryWrite(Unstable("Первое. Второе Тре", 3f));
        await fake.WhenPendingCancelled();
        await Task.Delay(100);
        outputs.Should().HaveCount(1, "a cancelled speculation produces no output");
        outputs[0].Text.Should().Be("EN[Первое.]");

        source.Writer.Complete();
        await fake.WhenCalled(3);
        fake.Calls[2].Should().Be(" Второе Тре");
        fake.Respond(" Второе Тре");
        await runTask;
        outputs[^1].IsStable.Should().BeTrue();
        outputs[^1].Text.Should().Be("EN[Первое.] EN[ Второе Тре]");
        translator.RetranslatedCount.Should().Be(0, "the dropped speculation never completed");
    }

    [Fact]
    public async Task AnEmptyTranslationPassesTheClauseThrough()
    {
        var fake = new FakeTranslate();
        var source = Channel.CreateUnbounded<Transcript>();
        var translator = new ClauseTranslator(fake.Translate, NullLogger.Instance);
        var outputs = new List<Transcript>();
        var runTask = Collect(translator.Run(source.Reader.ReadAllAsync(), CancellationToken.None), outputs);

        source.Writer.TryWrite(Stable("Привет, как дела?", 2f));
        await fake.WhenCalled(1);
        fake.RespondWith("");
        await WhenCount(outputs, 1);
        outputs[0].IsStable.Should().BeTrue();
        outputs[0].Text.Should().Be("Привет, как дела?");
        outputs[0].TimeRange.End.Should().BeApproximately(2f, 0.01f, "the clause's end time still enters the map");

        source.Writer.Complete();
        await runTask;
        outputs.Should().HaveCount(1, "an empty translation adds no speculative tail");
    }

    // Helpers

    private static Transcript Stable(string text, float endTime)
        => Unstable(text, endTime) with { IsStable = true };

    private static Transcript Unstable(string text, float endTime)
        => new(text, new LinearMap(Vector2.Zero, new Vector2(text.Length, endTime)), []);

    private static async Task Collect(IAsyncEnumerable<Transcript> transcripts, List<Transcript> outputs)
    {
        await foreach (var transcript in transcripts) {
            lock (outputs)
                outputs.Add(transcript);
        }
    }

    private static Task WhenCount(List<Transcript> outputs, int count)
        => WaitUntil(() => {
            lock (outputs)
                return outputs.Count >= count;
        }, () => {
            lock (outputs)
                return $"{outputs.Count} of {count} outputs";
        });

    private static async Task WaitUntil(Func<bool> condition, Func<string> describeState)
    {
        var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (!condition()) {
            if (DateTime.UtcNow > deadline)
                throw new TimeoutException($"Timed out waiting, state: {describeState()}");

            await Task.Delay(10);
        }
    }

    // One pending call at a time is enough: the lane is sequential by design
    private sealed class FakeTranslate
    {
        private readonly Lock _lock = new();
        private TaskCompletionSource<string>? _pending;

        public List<string> Calls { get; } = [];
        public List<TranslationResult[]> Contexts { get; } = [];

        public Task<string> Translate(string clause, TranslationResult[] context, CancellationToken cancellationToken)
        {
            lock (_lock) {
                Calls.Add(clause);
                Contexts.Add(context);
                var pending = TaskCompletionSourceExt.New<string>();
                _pending = pending;
                cancellationToken.Register(() => pending.TrySetCanceled(cancellationToken));
                return pending.Task;
            }
        }

        public void Respond(string clause)
            => RespondWith($"EN[{clause}]");

        public void RespondWith(string translated)
        {
            lock (_lock)
                _pending!.TrySetResult(translated);
        }

        public void Fail(Exception error)
        {
            lock (_lock)
                _pending!.TrySetException(error);
        }

        public Task WhenCalled(int count)
            => WaitUntil(() => {
                lock (_lock)
                    return Calls.Count >= count;
            }, () => {
                lock (_lock)
                    return $"{Calls.Count} of {count} calls";
            });

        public bool IsPendingCancelled {
            get {
                lock (_lock)
                    return _pending?.Task.IsCanceled == true;
            }
        }

        public Task WhenPendingCancelled()
            => WaitUntil(() => IsPendingCancelled, () => {
                lock (_lock)
                    return $"pending call status: {_pending?.Task.Status}";
            });
    }
}
