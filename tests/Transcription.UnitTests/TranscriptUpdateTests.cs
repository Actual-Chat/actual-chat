namespace ActualChat.Transcription.UnitTests;

public class TranscriptUpdateTests : TestBase
{
    public TranscriptUpdateTests(ITestOutputHelper @out) : base(@out) { }

    [Fact]
    public void SimpleTest1()
    {
        var transcript = new Transcript();
        var diff = new Transcript("раз", new (0, 0, 3, 1.86f));
        transcript = transcript.WithDiff(diff);
        diff = new("рад", new (0, 0, 3, 1.92f));
        transcript = transcript.WithDiff(diff);
        diff = new("работа", new (0, 0, 6, 1.98f));
        transcript = transcript.WithDiff(diff);
        diff = new("разбор", new (0, 0, 6, 2.04f));
        transcript = transcript.WithDiff(diff);
        diff = new("работа", new (0, 0, 6, 2.22f));
        transcript = transcript.WithDiff(diff);
        diff = new("раз-два-три", new (0, 0, 11, 2.28f));
        transcript = transcript.WithDiff(diff);
        diff = new("раз-два-три-четыре", new (0, 0, 18, 2.76f));
        transcript = transcript.WithDiff(diff);
        diff = new("раз-два-три-четыре", new (0, 0, 18, 3.36f));
        transcript = transcript.WithDiff(diff);
        diff = new("раз-два-три-четыре-пять", new (0, 0, 23, 3.42f));
        transcript = transcript.WithDiff(diff);
        diff = new("раз-два-три-четыре-пять", new (0, 0, 23, 4.02f));
        transcript = transcript.WithDiff(diff);
        diff = new(" Вышел", new (23, 4f, 29, 4.02f));
        transcript = transcript.WithDiff(diff);
        diff = new(" Вышел зайчик", new (23, 4f, 36, 5.52f));
        transcript = transcript.WithDiff(diff);
        diff = new(" вышел зайчик погулять", new (23, 4f, 45, 7.44f));
        transcript = transcript.WithDiff(diff);
        diff = new(" вышел зайчик погулять Вдруг откуда", new (23, 4f, 58, 8.34f));
        transcript = transcript.WithDiff(diff);
        Out.WriteLine(transcript.ToString());
    }

    [Fact]
    public void SimpleTest2()
    {
        var extractor = new GoogleTranscriberState();
        var t = new Transcript();
        _ = extractor.AppendAlternative("раз-два-три-четыре-пять,", 4.68f);
        _ = extractor.AppendFinal("раз-два-три-четыре-пять, 67", 4.98f);
        t = extractor.AppendAlternative(" вот", 8.14f);
        Dump(t);
        t = extractor.AppendAlternative(" Вот это", 8.56f);
        Dump(t);
    }

    [Fact]
    public void SimpleTest3()
    {
        var extractor = new GoogleTranscriberState();
        var t = new Transcript();
        _ = extractor.AppendFinal("1", 1);
        Dump(extractor.LastFinal);
        _ = extractor.AppendFinal(" 2", 2);
        Dump(extractor.LastFinal);
        _ = extractor.AppendFinal(" 3", new LinearMap(3, 2, 5, 3));
        t = extractor.AppendFinal("", extractor.LastFinal.TimeRange.End);
        Dump(t);
        t.TextToTimeMap.Length.Should().Be(4);
    }

    [Fact]
    public void SimpleTest4()
    {
        var extractor = new GoogleTranscriberState();
        var text = Enumerable.Range(0, 100).Select(i => i.ToString()).ToDelimitedString();
        var rnd = new Random(0);
        for (var offset = 0; offset <= text.Length; offset += rnd.Next(3)) {
            var isFinal = rnd.Next(3) == 0;
            var finalTextLength = extractor.LastFinal.Text.Length;
            var t = isFinal
                ? extractor.AppendFinal(text[finalTextLength..offset], offset)
                : extractor.AppendAlternative(text[finalTextLength..offset], offset);
            var expected = text[..offset];

            t.Text.Should().Be(expected);
            t.TextToTimeMap.IsValid().Should().BeTrue();
            for (var i = 0; i <= offset; i++)
                t.TextToTimeMap.Map(i).Should().BeApproximately(i, 0.01f);
        }
    }

    [Fact]
    public void SimpleTest5()
    {
        var t1 = new Transcript("по", new LinearMap(8, 5.21f, 10, 9.24f));
        var t2 = new Transcript("пое", new LinearMap(8, 5.21f, 11, 9.24f));
        var t = t1.WithDiff(t2);
        Out.WriteLine(t.ToString());
        t.Text.Should().Be(t2.Text);

        var t3 = new Transcript(" поехали", new LinearMap(7, 3.57f, 15, 9.78f));
        t = t1.WithDiff(t3);
        Out.WriteLine(t.ToString());
        t.Text.Should().Be(t3.Text);
    }

    [Fact]
    public void SimpleTest6()
    {
        var t1 = new Transcript(" а нефиг", new LinearMap(7, 7, 15, 15));
        var t2= new Transcript(" а нефигa", new LinearMap(7, 7, 16, 16));
        var t = t1.WithDiff(t2);
        Out.WriteLine(t.ToString());
        t.Text.Should().Be(t2.Text);
    }

    [Fact]
    public void SimpleTest7()
    {
        var t1 = new Transcript(" поешь", new LinearMap(15, 0, 21, 1));
        var t2 = new Transcript(" поехал", new LinearMap(15, 0, 22, 1));
        var t = t1.WithDiff(t2);
        Out.WriteLine(t.ToString());
        t.Text.Should().Be(t2.Text);
    }

    [Fact]
    public void SerializationTest()
    {
        var o = new Transcript(" поешь", new LinearMap(15, 0, 21, 1));
        var s = o.PassThroughAllSerializers(Out);
        s.Text.Should().Be(o.Text);
        s.TextToTimeMap.Length.Should().Be(o.TextToTimeMap.Length);
    }

    private void Dump(Transcript transcript)
        => Out.WriteLine($"* {transcript}");
}
