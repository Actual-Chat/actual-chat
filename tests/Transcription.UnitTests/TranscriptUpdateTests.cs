namespace ActualChat.Transcription.UnitTests;

public class TranscriptUpdateTests : TestBase
{
    public TranscriptUpdateTests(ITestOutputHelper @out) : base(@out) { }

    [Fact]
    public void WithDiffTest()
    {
        var transcript = new Transcript();
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
        var state = new TranscriberState();
        var t = state.AppendUnstable("раз-два-три-четыре-пять,", 4.68f);
        t = state.AppendStable("раз-два-три-четыре-пять, 67", 4.98f);
        t = state.AppendUnstable(" вот", 8.14f);
        Dump(t);
        t = state.AppendUnstable(" Вот это", 8.56f);
        Dump(t);
        t.TimeMap.Data.Should().Equal(0, 0, 27, 4.98f, 35, 8.56f);
    }

    [Fact]
    public void TranscriberStateTest2()
    {
        var state = new TranscriberState();
        _ = state.AppendStable("1", 1);
        Dump(state.Stable);
        _ = state.AppendStable(" 2", 2);
        Dump(state.Stable);
        _ = state.AppendStable(" 3", new LinearMap(3, 2, 5, 3));
        var t = state.AppendStable("", state.Stable.TimeRange.End);
        Dump(t);
        t.TimeMap.Length.Should().Be(4);
    }

    [Fact]
    public void TranscriberUnstableTest()
    {
        var state = new TranscriberState();
        _ = state.AppendUnstable("1", 1);
        Dump(state.Stable);
        var t = state.AppendUnstable(" 2", 2);
        Dump(state.Stable);
        t = state.MarkStable();
        t.TimeMap.Length.Should().Be(2);
    }

    [Fact]
    public void RandomTranscriberStateTest()
    {
        var extractor = new TranscriberState();
        var text = Enumerable.Range(0, 100).Select(i => i.ToString()).ToDelimitedString();
        var rnd = new Random(0);
        for (var offset = 0; offset <= text.Length; offset += rnd.Next(3)) {
            var isFinal = rnd.Next(3) == 0;
            var finalTextLength = extractor.Stable.Text.Length;
            var t = isFinal
                ? extractor.AppendStable(text[finalTextLength..offset], offset)
                : extractor.AppendUnstable(text[finalTextLength..offset], offset);
            var expected = text[..offset];

            t.Text.Should().Be(expected);
            t.TimeMap.IsValid().Should().BeTrue();
            for (var i = 0; i <= offset; i++)
                t.TimeMap.Map(i).Should().BeApproximately(i, 0.01f);
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
