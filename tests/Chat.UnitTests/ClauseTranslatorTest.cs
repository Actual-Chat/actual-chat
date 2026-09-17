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

        source.Writer.Complete();
        await runTask;
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

        source.Writer.Complete();
        await runTask;
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
        fake.Respond(" Второе.");
        await WhenCount(outputs, 2);
        outputs[0].Text.Should().Be("EN[Первое.]");
        outputs[1].Text.Should().Be("EN[Первое.] EN[ Второе.]");
        outputs.Should().OnlyContain(t => t.IsStable);

        source.Writer.Complete();
        await runTask;
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

    private static async Task WhenCount(List<Transcript> outputs, int count)
    {
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        while (true) {
            lock (outputs)
                if (outputs.Count >= count)
                    return;
            await Task.Delay(10, cts.Token);
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
        {
            lock (_lock)
                _pending!.TrySetResult($"EN[{clause}]");
        }

        public void Fail(Exception error)
        {
            lock (_lock)
                _pending!.TrySetException(error);
        }

        public async Task WhenCalled(int count)
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            while (true) {
                lock (_lock)
                    if (Calls.Count >= count)
                        return;
                await Task.Delay(10, cts.Token);
            }
        }
    }
}
