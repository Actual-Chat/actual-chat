using System.Globalization;
using ActualChat.Transcription.Google;

namespace ActualChat.Transcription.UnitTests;

public class TranscriptUpdateTests(ITestOutputHelper @out) : TestBase(@out)
{
    [Fact]
    public void WithDiffTest()
    {
        var transcript = Transcript.New();
        var diff = new TranscriptDiff(new StringDiff(0, "раз"), new LinearMapDiff(new LinearMap(0, 0, 3, 1.86f)));
        transcript += diff;
        diff = new(new StringDiff(0, " вышел зайчик погулять Вдруг откуда"), new LinearMapDiff(new LinearMap(23, 4f, 58, 8.34f)));
        transcript += diff;
        Out.WriteLine(transcript.ToString());

        transcript.Text.Length.Should().Be(35);
        transcript.TimeMap.Data.Should()
            .Equal(0, 0, 3, 1.86f, 23, 4, 58, 8.34f);

        diff = new(new StringDiff(35, "x"), new LinearMapDiff(new LinearMap(0, 1, 23, 4f, 58, 8.34f)));
        transcript += diff;
        Out.WriteLine(transcript.ToString());

        transcript.Text.Length.Should().Be(36);
        transcript.TimeMap.Data.Should()
            .Equal(0, 1, 23, 4, 58, 8.34f);
    }

    [Fact]
    public void TranscriberStateTest1()
    {
        var state = new GoogleTranscribeState(null!, null!, null!);
        state.Append("раз-два-три-четыре-пять,", 4.68f);
        state.Append("раз-два-три-четыре-пять, 67", 4.98f).MakeStable();
        state.Append(" вот", 8.14f);
        Dump(state.Stable);
        state.Append(" Вот это", 8.56f).MakeStable();
        Dump(state.Stable);
        state.Stable.TimeMap.Data.Should().Equal(0, 0, 24, 4.68f, 27, 4.98f, 31, 8.14f, 35, 8.56f);
    }

    [Fact]
    public void TranscriberStateTest2()
    {
        var state = new GoogleTranscribeState(null!, null!, null!);
        state.Append("1", 1).MakeStable();
        state.Append(" 2", 2).MakeStable();
        state.Append(" 3", new LinearMap(3, 2, 5, 3)).MakeStable();
        state.Append("", state.Stable.TimeRange.End + 1f).MakeStable();
        Dump(state.Stable);
        state.Stable.TimeMap.Length.Should().Be(4);
        state.Stable.TimeRange.End.Should().BeApproximately(3f, 0.01f);
    }

    [Fact]
    public void TranscriberUnstableTest()
    {
        var state = new GoogleTranscribeState(null!, null!, null!);
        state.Append("1", 1);
        state.Append(" 2", 2).MakeStable();
        state.Stable.TimeMap.Length.Should().Be(3);
    }

    [Fact]
    public void RandomTranscriberStateTest()
    {
        var state = new GoogleTranscribeState(null!, null!, null!);
        state.Append("X", null).MakeStable();
        var text = Enumerable.Range(0, 100).Select(i => i.ToString(CultureInfo.InvariantCulture)).ToDelimitedString("-");
        var rnd = new Random(0);
        var lastOffset = 1;
        for (var offset = 1; offset <= text.Length; offset += 1 + rnd.Next(3)) {
            var isStable = rnd.Next(3) == 0;
            var suffix = " " + text[lastOffset..offset];
            state.Append(suffix, offset).MakeStable(isStable);

            state.Unstable.Text.Should().EndWith(suffix);
            state.Unstable.TimeMap.IsValid().Should().BeTrue();
            lastOffset = offset;
        }
    }

    [Fact]
    public void SerializationTest()
    {
        var symbol = (Symbol)"Test";
        symbol.AssertPassesThroughAllSerializers();

        var o = new Transcript(" поешь", new LinearMap(15, 0, 21, 1));
        var s = o.PassThroughAllSerializers(Out);
        s.Text.Should().Be(o.Text);
        s.TimeMap.Length.Should().Be(o.TimeMap.Length);
    }

    private void Dump(Transcript transcript)
        => Out.WriteLine($"* {transcript}");
}
