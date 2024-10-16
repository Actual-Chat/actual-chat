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
            new Transcript("Hey!", LinearMap.Zero),
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
            return new Transcript(newText, t.TimeMap.Append(new Vector2(newLength, newLength)));
        }

        Transcript Shrink1(Transcript t)
        {
            if (t.Text.Length == 0)
                return t;

            var newText = t.Text[..^1];
            var newMap = t.TimeMap.GetPrefix(newText.Length, Transcript.TimeMapEpsilon.X);
            newMap.Length.Should().Be(newText.Length + 1);
            return new Transcript(newText, newMap);
        }

        char GetRandomChar() => (char)('0' + rnd.Next(10));
    }

    // Private methods

    private async Task CheckDiff(string title, IReadOnlyList<Transcript> transcripts)
    {
        var diffs = transcripts.ToTranscriptDiffs();
        var restored = diffs.ToTranscripts().ToList();

        Out.WriteLine($"{title}:");
        for (var i = 0; i < transcripts.Count; i++) {
            var t = transcripts[i];
            var r = restored.GetValueOrDefault(i);
            r.Should().NotBeNull();
            Out.WriteLine($"- {t} -> {r}");
            r!.IsIdenticalTo(t).Should().BeTrue();
        }
        restored.Count.Should().Be(transcripts.Count);

        diffs = await transcripts.ToAsyncEnumerable().ToTranscriptDiffs().ToListAsync();
        restored = await diffs.ToAsyncEnumerable().ToTranscripts().ToListAsync();
        restored.Count.Should().Be(transcripts.Count);
        for (var i = 0; i < transcripts.Count; i++)
            restored[i].IsIdenticalTo(transcripts[i]).Should().BeTrue();
    }
}
