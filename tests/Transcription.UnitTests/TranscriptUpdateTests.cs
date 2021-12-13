namespace ActualChat.Transcription.UnitTests;

public class TranscriptUpdateTests : TestBase
{
    public TranscriptUpdateTests(ITestOutputHelper @out) : base(@out) { }

    [Fact]
    public void SimpleTest1()
    {
        var transcript = new Transcript();
        var diff = new Transcript("раз", new (new[] { 0, 3d }, new[] { 0, 1.86 }));
        transcript = transcript.WithDiff(diff);
        diff = new("рад", new (new[] { 0, 3d }, new[] { 0, 1.92 }));
        transcript = transcript.WithDiff(diff);
        diff = new("работа", new (new[] { 0, 6d }, new[] { 0, 1.98 }));
        transcript = transcript.WithDiff(diff);
        diff = new("разбор", new (new[] { 0, 6d }, new[] { 0, 2.04 }));
        transcript = transcript.WithDiff(diff);
        diff = new("работа", new (new[] { 0, 6d }, new[] { 0, 2.22 }));
        transcript = transcript.WithDiff(diff);
        diff = new("раз-два-три", new (new[] { 0, 11d }, new[] { 0, 2.28 }));
        transcript = transcript.WithDiff(diff);
        diff = new("раз-два-три-четыре", new (new[] { 0, 18d }, new[] { 0, 2.76 }));
        transcript = transcript.WithDiff(diff);
        diff = new("раз-два-три-четыре", new (new[] { 0, 18d }, new[] { 0, 3.36 }));
        transcript = transcript.WithDiff(diff);
        diff = new("раз-два-три-четыре-пять", new (new[] { 0, 23d }, new[] { 0, 3.42 }));
        transcript = transcript.WithDiff(diff);
        diff = new("раз-два-три-четыре-пять", new (new[] { 0, 23d }, new[] { 0, 4.02 }));
        transcript = transcript.WithDiff(diff);
        diff = new(
            "раз-два-три-четыре-пять Вышел",
            new (new[] { 23d, 29d }, new[] { 4, 4.92 }));
        transcript = transcript.WithDiff(diff);
        diff = new(
            "раз-два-три-четыре-пять Вышел зайчик",
            new (new[] { 23d, 36d }, new[] { 4, 5.52 }));
        transcript = transcript.WithDiff(diff);
        diff = new(
            "раз-два-три-четыре-пять вышел зайчик погулять",
            new (new[] { 23d, 45d }, new[] { 4, 7.44 }));
        transcript = transcript.WithDiff(diff);
        diff = new(
            "раз-два-три-четыре-пять вышел зайчик погулять Вдруг откуда",
            new (new[] { 23d, 58d }, new[] { 4, 8.34 }));
        transcript = transcript.WithDiff(diff);
        Out.WriteLine(transcript.ToString());
    }

    [Fact]
    public void SimpleTest2()
    {
        var extractor = new GoogleTranscriberState();
        var t = new Transcript();
        _ = extractor.AppendAlternative("раз-два-три-четыре-пять,", 4.68);
        _ = extractor.AppendFinal("раз-два-три-четыре-пять, 67", 4.98);
        t = extractor.AppendAlternative(" вот", 8.140);
        Dump(t);
        t = extractor.AppendAlternative(" Вот это", 8.560);
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
        _ = extractor.AppendFinal(" 3", new LinearMap(new double[] {3 , 5}, new double[] {2 , 3}));
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
                t.TextToTimeMap.Map(i).Should().BeApproximately(i, 0.01);
        }
    }

    [Fact]
    public void SimpleTest5()
    {
        var t1 = new Transcript("по",
            new LinearMap(new [] {8d, 10}, new [] {5.21d, 9.24}));
        var t2 = new Transcript("пое",
            new LinearMap(new [] {8d, 11}, new [] {5.21d, 9.24}));
        var t = t1.WithDiff(t2);
        Out.WriteLine(t.ToString());
        t.Text.Should().Be(t2.Text);

        var t3 = new Transcript(" поехали",
            new LinearMap(new [] {7d, 15}, new [] {3.57, 9.78}));
        t = t1.WithDiff(t3);
        Out.WriteLine(t.ToString());
        t.Text.Should().Be(t3.Text);
    }

    [Fact]
    public void SimpleTest6()
    {
        var t1 = new Transcript(" а нефиг",
            new LinearMap(new [] {7d, 15}, new [] {7d, 15}));
        var t2= new Transcript(" а нефигa",
            new LinearMap(new [] {7d, 16}, new [] {7d, 16}));
        var t = t1.WithDiff(t2);
        Out.WriteLine(t.ToString());
        t.Text.Should().Be(t2.Text);
    }

    [Fact]
    public void SimpleTest7()
    {
        var t1 = new Transcript(" поешь",
            new LinearMap(new [] {15d, 21}, new [] {0d, 1}));
        var t2 = new Transcript(" поехал",
            new LinearMap(new [] {15d, 22}, new [] {0d, 1}));
        var t = t1.WithDiff(t2);
        Out.WriteLine(t.ToString());
        t.Text.Should().Be(t2.Text);
    }

    private void Dump(Transcript transcript)
        => Out.WriteLine($"* {transcript}");
}
