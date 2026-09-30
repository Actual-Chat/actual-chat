using System.Threading.Channels;
using ActualChat.Chat.ML;

namespace ActualChat.Chat.UnitTests.Coach;

public class SpeechLiveTaggerTest(ITestOutputHelper @out) : TestBase(@out)
{
    private static readonly LiveTagOptions Options = new (10, 40, 30, 12, true);
    private static readonly TimeSpan Wait = TimeSpan.FromSeconds(10);

    private static string Sentence(int n)
        => $"Sentence {n} has um a few plain words in it right here.";

    private sealed class Rig
    {
        private readonly Channel<string> _texts = Channel.CreateUnbounded<string>();
        private int _progress;

        public List<(string Text, string? Context)> Calls { get; } = new();
        public List<(string Text, ApiArray<SpeechSpan> Spans)> Progress { get; } = new();
        public bool Fails { get; set; }

        public Task<LiveTagResult> Start(LiveTagOptions? options = null)
            => SpeechLiveTagger.Run(
                _texts.Reader.ReadAllAsync(),
                (text, context, _) => {
                    lock (Calls)
                        Calls.Add((text, context));
                    return Task.FromResult(Fails ? null : FindUm(text));
                },
                (text, spans) => {
                    lock (Progress)
                        Progress.Add((text, spans));
                    Interlocked.Increment(ref _progress);
                },
                options ?? Options,
                default);

        public void Say(string text) => _texts.Writer.TryWrite(text);
        public void End() => _texts.Writer.TryComplete();

        public Task WhenProgress(int count)
            => TestWait.WhenPolled(() => Volatile.Read(ref _progress).Should().BeGreaterThanOrEqualTo(count), Wait);

        private static ApiArray<SpeechSpan>? FindUm(string chunk)
        {
            var i = chunk.IndexOf("um");
            return i < 0
                ? ApiArray<SpeechSpan>.Empty
                : ApiArray.New(new SpeechSpan(SpeechSpanKind.FilledPause, "um", i, 2, ApiArray<string>.Empty));
        }
    }

    [Fact]
    public async Task RunShouldTagCompletedSentencesWhileTheTextIsStillGrowing()
    {
        // arrange
        var rig = new Rig();
        var run = rig.Start();

        // act: two full sentences and the start of a third
        rig.Say(Sentence(1) + " " + Sentence(2) + " Sentence 3 is st");
        await rig.WhenProgress(1);

        // assert: the stream is still open, yet the first sentences are tagged
        run.IsCompleted.Should().BeFalse();
        rig.Calls.Should().ContainSingle().Which.Text.Should().Contain("Sentence 1").And.Contain("Sentence 2");
        rig.Calls[0].Text.Should().NotContain("Sentence 3");
        rig.End();
        await run;
    }

    [Fact]
    public async Task RunShouldNotTagASentenceUntilTextFollowsIt()
    {
        // arrange
        var rig = new Rig();
        var run = rig.Start();

        // act: the sentence ends the text, nothing has come after it
        rig.Say(Sentence(1));
        await Task.Delay(300);

        // assert
        rig.Calls.Should().BeEmpty("the closing punctuation may still change");
        rig.Say(Sentence(1) + " Then");
        await rig.WhenProgress(1);
        rig.End();
        await run;
    }

    [Fact]
    public async Task RunShouldGiveTheSentenceBeforeAsContextAndReturnAbsoluteOffsets()
    {
        // arrange
        var rig = new Rig();
        var run = rig.Start();
        var first = Sentence(1) + " " + Sentence(2) + " ";
        var second = Sentence(3) + " " + Sentence(4) + " ";

        // act
        rig.Say(first + "Then");
        await rig.WhenProgress(1);
        rig.Say(first + second + "Then");
        await rig.WhenProgress(2);
        rig.Say(first + second + "Then it ends here.");
        rig.End();
        var result = await run;

        // assert
        rig.Calls[0].Context.Should().BeNull();
        rig.Calls[1].Context.Should().Be(Sentence(2));
        result.IsComplete.Should().BeTrue();
        result.Text.Should().Be(first + second + "Then it ends here.");
        result.Spans.Should().OnlyContain(s => result.Text.Substring(s.Start, s.Length) == "um");
        result.Spans.Should().HaveCount(2, "the third chunk, the tail, has no um");
        rig.Calls.Should().HaveCount(3);
    }

    [Fact]
    public async Task RunShouldTagTheTailWhenTheStreamEnds()
    {
        // arrange
        var rig = new Rig();
        var run = rig.Start();

        // act
        rig.Say("Short um tail.");
        rig.End();
        var result = await run;

        // assert
        rig.Calls.Should().ContainSingle().Which.Text.Should().Be("Short um tail.");
        result.IsComplete.Should().BeTrue();
        result.Spans.Should().ContainSingle();
    }

    [Fact]
    public async Task RunShouldReportAFailedChunkAsIncomplete()
    {
        // arrange
        var rig = new Rig { Fails = true };
        var run = rig.Start();

        // act
        rig.Say(Sentence(1) + " " + Sentence(2) + " Then");
        rig.End();
        var result = await run;

        // assert
        result.IsComplete.Should().BeFalse("the settled text must then be tagged as a whole");
        result.Spans.Should().BeEmpty();
    }

    [Fact]
    public async Task RunShouldStopAtTheChunkLimitButKeepWhatItHas()
    {
        // arrange
        var rig = new Rig();
        var run = rig.Start(Options with { MaxChunks = 1 });
        var text = string.Join(" ", Enumerable.Range(1, 6).Select(Sentence));

        // act
        rig.Say(text + " Then");
        rig.End();
        var result = await run;

        // assert
        rig.Calls.Should().HaveCount(1);
        result.IsComplete.Should().BeFalse();
        result.Spans.Should().HaveCount(1);
    }

    [Fact]
    public async Task RunShouldTagAgainFromTheFirstChangedSentenceWhenTheTaggedTextIsRevised()
    {
        // arrange
        var rig = new Rig();
        var run = rig.Start();
        var revised = "Changed sentence two has um different words in it now.";

        // act: the second sentence, already tagged, is rewritten
        rig.Say(Sentence(1) + " " + Sentence(2) + " Then");
        await rig.WhenProgress(1);
        rig.Say(Sentence(1) + " " + revised + " Then");
        rig.End();
        var result = await run;

        // assert: the first sentence keeps its mark, the rewritten one is tagged again
        result.IsComplete.Should().BeTrue();
        result.Restarts.Should().Be(1);
        result.Spans.Select(s => s.Start).Should().Equal(
            Sentence(1).IndexOf("um"),
            Sentence(1).Length + 1 + revised.IndexOf("um"));
        rig.Calls[1].Text.Should().StartWith(revised);
        rig.Calls[1].Context.Should().Be(Sentence(1));
    }

    [Fact]
    public async Task RunShouldSendTheFirstChunksEarlyAndTheLaterOnesAtFullSize()
    {
        // arrange: a sentence has 11 words
        var rig = new Rig();
        var run = rig.Start(new LiveTagOptions(30, 60, 30, 12, true) { FirstMinWords = 5, FastChunks = 2 });
        string Text(int count) => string.Join(" ", Enumerable.Range(1, count).Select(Sentence)) + " Then";

        // act & assert: the first two sentences go out one by one
        rig.Say(Text(1));
        await rig.WhenProgress(1);
        rig.Say(Text(2));
        await rig.WhenProgress(2);
        rig.Calls.Select(c => c.Text.Trim()).Should().Equal(Sentence(1), Sentence(2));

        // act & assert: after that a chunk waits for the full size
        rig.Say(Text(4));
        await Task.Delay(200);
        rig.Calls.Should().HaveCount(2, "22 more words are fewer than the 30 a later chunk needs");
        rig.Say(Text(5));
        await rig.WhenProgress(3);
        rig.Calls[2].Text.Trim().Should().Be(string.Join(" ", Enumerable.Range(3, 3).Select(Sentence)));
        rig.End();
        (await run).Outcome.Should().Be(LiveTagOutcome.Complete);
    }
}
