using System.Numerics;

namespace ActualChat.Transcription.UnitTests;

public class TranscriptDiffTest(ITestOutputHelper @out) : TestBase(@out)
{
    [Fact]
    public async Task NegativeDiffTest()
    {
        var transcripts = new[] {
            Transcript.Ellipsis,
            Transcript.Empty,
            new Transcript("Hey!", LinearMap.Zero, []),
        };
        await CheckDiff("Negative diff:", transcripts);
    }

    [Fact]
    public async Task RandomTest()
    {
        var rnd = new Random();
        for (var i = 0; i < 100; i++) {
            var l = rnd.Next(10);
            var transcripts = new List<Transcript>();
            var t = Transcript.Empty;
            for (var j = 0; j < l; j++) {
                var maxDelta = j == 0 ? l : 3;
                var delta = rnd.Next(2*maxDelta + 1) - maxDelta;
                t = Grow(t, delta);
                transcripts.Add(t);
            }
            await CheckDiff($"Test {i}", transcripts);
        }

        Transcript Grow(Transcript t, int size)
        {
            if (size < 0)
                for (var i = 0; i > size; i--)
                    t = Shrink1(t);
            else
                for (var i = 0; i < size; i++)
                    t = Grow1(t);
            return t;
        }

        Transcript Grow1(Transcript t)
        {
            var newText = t.Text + GetRandomChar();
            var newLength = newText.Length;
            return new Transcript(newText, t.TimeMap.Append(new Vector2(newLength, newLength)), t.Languages);
        }

        Transcript Shrink1(Transcript t)
        {
            if (t.Text.Length == 0)
                return t;

            var newText = t.Text[..^1];
            var newMap = t.TimeMap.GetPrefix(newText.Length, Transcript.TimeMapEpsilon.X);
            newMap.Length.Should().Be(newText.Length + 1);
            return new Transcript(newText, newMap, t.Languages);
        }

        char GetRandomChar() => (char)('0' + rnd.Next(10));
    }

    [Fact]
    public void DiffShouldCarryLanguagesAndStability()
    {
        // arrange
        var unstable = new Transcript("Hello", LinearMap.Zero, [Languages.Russian]);
        var stable = unstable with { IsStable = true };

        // act
        var grown = Transcript.Empty + (unstable - Transcript.Empty);
        var promotion = stable - unstable;
        var promoted = grown + promotion;

        // assert
        grown.Languages.Should().Equal([Languages.Russian],
            "detected languages don't fit a text diff, so the diff carries them explicitly");
        grown.IsStable.Should().BeFalse();
        promotion.IsNone.Should().BeTrue("the text didn't change");
        promoted.IsStable.Should().BeTrue("an empty diff still promotes the transcript it's applied to");
        promoted.Languages.Should().Equal([Languages.Russian]);
    }

    [Fact]
    public void DiffShouldCarryTheSegmentEnd()
    {
        // arrange
        var unstable = new Transcript("Да", LinearMap.Zero.Append(new Vector2(2, 1f)), [Languages.Russian]);
        var stable = unstable with { IsStable = true };
        var segmentEnd = stable with { IsSegmentEnd = true };
        var next = new Transcript("Да Как", stable.TimeMap.Append(new Vector2(6, 1.8f)), [Languages.Russian]);

        // act
        var grown = Transcript.Empty + (unstable - Transcript.Empty);
        var promoted = grown + (stable - unstable);
        var endedDiff = segmentEnd - stable;
        var ended = promoted + endedDiff;
        var repeated = ended + (segmentEnd - segmentEnd);
        var continued = ended + (next - segmentEnd);
        var collapsed = Transcript.Empty + (segmentEnd - Transcript.Empty);

        // assert
        grown.IsSegmentEnd.Should().BeFalse();
        promoted.IsSegmentEnd.Should().BeFalse();
        endedDiff.IsNone.Should().BeTrue("the text and the map didn't change");
        ended.IsSegmentEnd.Should().BeTrue("a flags-only diff carries the segment end like it carries stability");
        ended.IsStable.Should().BeTrue();
        repeated.IsSegmentEnd.Should().BeTrue();
        continued.IsSegmentEnd.Should().BeFalse("the flag is per transcript: new speech is a segment in progress");
        collapsed.IsSegmentEnd.Should().BeTrue("a late reader's collapsed diff keeps the fold's flag");
    }

    [Fact]
    public async Task ToTranscriptsStopsOnCancellationOfAnEndlessSource()
    {
        // A remote RpcStream enumerator ends only with the stream, so the token passed to
        // ToTranscripts is what lets a reader leave a stream that is still being produced.
        var diffs = Channel.CreateUnbounded<TranscriptDiff>();
        var hello = new Transcript("Hello", LinearMap.Zero.Append(new Vector2(5, 5)), []);
        diffs.Writer.TryWrite(hello - Transcript.Empty);
        using var cts = new CancellationTokenSource();
        var transcripts = new List<Transcript>();

        var readTask = Task.Run(async () => {
            await foreach (var t in diffs.Reader.ReadAllAsync().ToTranscripts(cts.Token))
                transcripts.Add(t);
        });
        await TestWait.WhenPolled(() => transcripts.Should().HaveCount(1), TimeSpan.FromSeconds(5));
        cts.Cancel();

        await FluentActions.Awaiting(() => readTask.WaitAsync(TimeSpan.FromSeconds(5)))
            .Should().ThrowAsync<OperationCanceledException>();
        transcripts.Single().Text.Should().Be("Hello");
    }

    // Private methods

    private async Task CheckDiff(string title, IReadOnlyList<Transcript> transcripts)
    {
        var diffs = transcripts.ToTranscriptDiffs();
        var restored = diffs.ToTranscripts().ToList();

        WriteLine($"{title}:");
        for (var i = 0; i < transcripts.Count; i++) {
            var t = transcripts[i];
            var r = restored.GetValueOrDefault(i);
            r.Should().NotBeNull();
            WriteLine($"- {t} -> {r}");
            r.IsIdenticalTo(t).Should().BeTrue();
        }
        restored.Count.Should().Be(transcripts.Count);

        diffs = await transcripts.ToAsyncEnumerable().ToTranscriptDiffs().ToListAsync();
        restored = await diffs.ToAsyncEnumerable().ToTranscripts().ToListAsync();
        restored.Count.Should().Be(transcripts.Count);
        for (var i = 0; i < transcripts.Count; i++)
            restored[i].IsIdenticalTo(transcripts[i]).Should().BeTrue();
    }
}
