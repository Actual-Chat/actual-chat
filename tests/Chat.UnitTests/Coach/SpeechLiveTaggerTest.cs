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
            var i = chunk.IndexOf("um", StringComparison.Ordinal);
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
    public async Task RunShouldDropTheSpansWhenTheTaggedTextWasRevised()
    {
        // arrange
        var rig = new Rig();
        var run = rig.Start();

        // act: the words already tagged are rewritten in the final text
        rig.Say(Sentence(1) + " " + Sentence(2) + " Then");
        await rig.WhenProgress(1);
        rig.Say("Something else entirely was said, um, in the end.");
        rig.End();
        var result = await run;

        // assert
        result.IsComplete.Should().BeFalse();
        result.Text.Should().Be("Something else entirely was said, um, in the end.");
        result.Spans.Should().BeEmpty();
    }
}
