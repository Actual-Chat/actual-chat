namespace ActualChat.Transcription.UnitTests;

public class TranscriptUpdateTests : TestBase
{
    public TranscriptUpdateTests(ITestOutputHelper @out) : base(@out) { }

    [Fact]
    public void WithDiffTest1()
    {
        var transcript = new Transcript();
        var diff = new Transcript("раз", new (0, 0, 3, 1.86f), true, true);
        transcript = transcript.WithDiff(diff);
        diff = new("рад", new (0, 0, 3, 1.92f), true, true);
        transcript = transcript.WithDiff(diff);
        diff = new("работа", new (0, 0, 6, 1.98f), true, true);
        transcript = transcript.WithDiff(diff);
        diff = new("разбор", new (0, 0, 6, 2.04f), true, true);
        transcript = transcript.WithDiff(diff);
        diff = new("работа", new (0, 0, 6, 2.22f), true, true);
        transcript = transcript.WithDiff(diff);
        diff = new("раз-два-три", new (0, 0, 11, 2.28f), true, true);
        transcript = transcript.WithDiff(diff);
        diff = new("раз-два-три-четыре", new (0, 0, 18, 2.76f), true, true);
        transcript = transcript.WithDiff(diff);
        diff = new("раз-два-три-четыре", new (0, 0, 18, 3.36f), true, true);
        transcript = transcript.WithDiff(diff);
        diff = new("раз-два-три-четыре-пять", new (0, 0, 23, 3.42f), true, true);
        transcript = transcript.WithDiff(diff);
        diff = new("раз-два-три-четыре-пять", new (0, 0, 23, 4.02f), true, true);
        transcript = transcript.WithDiff(diff);
        diff = new(" Вышел", new (23, 4f, 29, 4.02f), true, true);
        transcript = transcript.WithDiff(diff);
        diff = new(" Вышел зайчик", new (23, 4f, 36, 5.52f), true, true);
        transcript = transcript.WithDiff(diff);
        diff = new(" вышел зайчик погулять", new (23, 4f, 45, 7.44f), true, true);
        transcript = transcript.WithDiff(diff);
        diff = new(" вышел зайчик погулять Вдруг откуда", new (23, 4f, 58, 8.34f), true, true);
        transcript = transcript.WithDiff(diff);
        Out.WriteLine(transcript.ToString());

        transcript.Text.Length.Should().Be(58);
        transcript.TimeMap.Data.Should()
            .Equal(0, 0, 23, 4, 58, 8.34f);
    }

    [Fact]
    public void WithDiffTest2()
    {
        var t1 = new Transcript("по", new LinearMap(8, 5.21f, 10, 9.24f));
        var t2 = new Transcript("пое", new LinearMap(8, 5.21f, 11, 9.24f), true, true);
        var t = t1.WithDiff(t2);
        Out.WriteLine(t.ToString());
        t.Text.Should().Be(t2.Text);

        var t3 = new Transcript(" поехали", new LinearMap(7, 3.57f, 15, 9.78f), true, true);
        t = t1.WithDiff(t3);
        Out.WriteLine(t.ToString());
        t.Text.Should().Be(t3.Text);
    }

    [Fact]
    public void WithDiffTest3()
    {
        var t1 = new Transcript(" а нефиг", new LinearMap(7, 7, 15, 15));
        var t2 = new Transcript(" а нефигa", new LinearMap(7, 7, 16, 16), true, true);
        var t = t1.WithDiff(t2);
        Out.WriteLine(t.ToString());
        t.Text.Should().Be(t2.Text);
    }

    [Fact]
    public void WithDiffTest4()
    {
        var t1 = new Transcript(" поешь", new LinearMap(15, 0, 21, 1));
        var t2 = new Transcript(" поехал", new LinearMap(15, 0, 22, 1), true, true);
        var t = t1.WithDiff(t2);
        Out.WriteLine(t.ToString());
        t.Text.Should().Be(t2.Text);
    }

    [Fact]
    public void TranscriberStateTest1()
    {
        var extractor = new TranscriberState();
        var t = new Transcript();
        t.IsStable.Should().BeFalse();
        t = extractor.AppendUnstable("раз-два-три-четыре-пять,", 4.68f);
        t.IsStable.Should().BeFalse();
        t = extractor.AppendStable("раз-два-три-четыре-пять, 67", 4.98f);
        t.IsStable.Should().BeTrue();
        t = extractor.AppendUnstable(" вот", 8.14f);
        Dump(t);
        t.IsStable.Should().BeFalse();
        t = extractor.AppendUnstable(" Вот это", 8.56f);
        Dump(t);
        t.IsStable.Should().BeFalse();
        t.TimeMap.Data.Should()
            .Equal(0, 0, 27, 4.98f, 35, 8.56f);
    }

    [Fact]
    public void TranscriberStateTest2()
    {
        var extractor = new TranscriberState();
        var t = new Transcript();
        _ = extractor.AppendStable("1", 1);
        Dump(extractor.Stable);
        _ = extractor.AppendStable(" 2", 2);
        Dump(extractor.Stable);
        _ = extractor.AppendStable(" 3", new LinearMap(3, 2, 5, 3));
        t = extractor.AppendStable("", extractor.Stable.TimeRange.End);
        Dump(t);
        t.IsStable.Should().BeTrue();
        t.TimeMap.Length.Should().Be(4);
    }

    [Fact]
    public void TranscriberUnstableTest()
    {
        var extractor = new TranscriberState();
        var t = new Transcript();
        _ = extractor.AppendUnstable("1", 1);
        Dump(extractor.Stable);
        t = extractor.AppendUnstable(" 2", 2);
        Dump(extractor.Stable);
        t.IsStable.Should().BeFalse();
        t = extractor.MarkStable();
        t.IsStable.Should().BeTrue();
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
