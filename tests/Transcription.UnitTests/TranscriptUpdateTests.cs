namespace ActualChat.Transcription.UnitTests;

public class TranscriptUpdateTests : TestBase
{
    public TranscriptUpdateTests(ITestOutputHelper @out) : base(@out) { }

    [Fact]
    public void SimpleTest1()
    {
        var transcript = new Transcript();
        var update = new Transcript
            { Text = "раз", TextToTimeMap = new (new[] { 0, 3d }, new[] { 0, 1.86 }) };
        transcript = transcript.WithUpdate(new (update));
        update = new() { Text = "рад", TextToTimeMap = new (new[] { 0, 3d }, new[] { 0, 1.92 }) };
        transcript = transcript.WithUpdate(new (update));
        update = new() { Text = "работа", TextToTimeMap = new (new[] { 0, 6d }, new[] { 0, 1.98 }) };
        transcript = transcript.WithUpdate(new (update));
        update = new() { Text = "разбор", TextToTimeMap = new (new[] { 0, 6d }, new[] { 0, 2.04 }) };
        transcript = transcript.WithUpdate(new (update));
        update = new() { Text = "работа", TextToTimeMap = new (new[] { 0, 6d }, new[] { 0, 2.22 }) };
        transcript = transcript.WithUpdate(new (update));
        update = new() { Text = "раз-два-три", TextToTimeMap = new (new[] { 0, 11d }, new[] { 0, 2.28 }) };
        transcript = transcript.WithUpdate(new (update));
        update = new() { Text = "раз-два-три-четыре", TextToTimeMap = new (new[] { 0, 18d }, new[] { 0, 2.76 }) };
        transcript = transcript.WithUpdate(new (update));
        update = new() { Text = "раз-два-три-четыре", TextToTimeMap = new (new[] { 0, 18d }, new[] { 0, 3.36 }) };
        transcript = transcript.WithUpdate(new (update));
        update = new() { Text = "раз-два-три-четыре-пять", TextToTimeMap = new (new[] { 0, 23d }, new[] { 0, 3.42 }) };
        transcript = transcript.WithUpdate(new (update));
        update = new() { Text = "раз-два-три-четыре-пять", TextToTimeMap = new (new[] { 0, 23d }, new[] { 0, 4.02 }) };
        transcript = transcript.WithUpdate(new (update));
        update = new() {
            Text = "раз-два-три-четыре-пять Вышел",
            TextToTimeMap = new (new[] { 23d, 29d }, new[] { 4, 4.92 }),
        };
        transcript = transcript.WithUpdate(new (update));
        update = new() {
            Text = "раз-два-три-четыре-пять Вышел зайчик",
            TextToTimeMap = new (new[] { 23d, 36d }, new[] { 4, 5.52 }),
        };
        transcript = transcript.WithUpdate(new (update));
        update = new() {
            Text = "раз-два-три-четыре-пять вышел зайчик погулять",
            TextToTimeMap = new (new[] { 23d, 45d }, new[] { 4, 7.44 }),
        };
        transcript = transcript.WithUpdate(new (update));
        update = new() {
            Text = "раз-два-три-четыре-пять вышел зайчик погулять Вдруг откуда",
            TextToTimeMap = new (new[] { 23d, 58d }, new[] { 4, 8.34 }),
        };
        transcript = transcript.WithUpdate(new (update));

        Out.WriteLine(transcript.ToString());
    }

    [Fact]
    public void SimpleTest2()
    {
        var extractor = new TranscriptUpdateExtractor();
        var t = new Transcript();
        var u = extractor.AppendAlternative("раз-два-три-четыре-пять,", 4.68);
        t = t.WithUpdate(u);
        u = extractor.AppendFinal("раз-два-три-четыре-пять, 67", 4.98);
        t = t.WithUpdate(u);
        u = extractor.AppendAlternative(" вот", 8.140);
        t = t.WithUpdate(u);
        Dump(t);
        u = extractor.AppendAlternative(" Вот это", 8.560);
        t = t.WithUpdate(u);
        Dump(t);
    }

    [Fact]
    public void SimpleTest3()
    {
        var extractor = new TranscriptUpdateExtractor();
        var text = Enumerable.Range(0, 100).Select(i => i.ToString()).ToDelimitedString();
        var rnd = new Random(0);
        var t = new Transcript();
        for (var offset = 0; offset <= text.Length; offset += rnd.Next(3)) {
            var isFinal = rnd.Next(3) == 0;
            var finalTextLength = extractor.LastFinal.Text.Length;
            var update = isFinal
                ? extractor.AppendFinal(text[finalTextLength..offset], offset)
                : extractor.AppendAlternative(text[finalTextLength..offset], offset);
            t = t.WithUpdate(update);
            var expected = text[..offset];

            t.Text.Should().Be(expected);
            t.TextToTimeMap.IsValid().Should().BeTrue();
            for (var i = 0; i <= offset; i++)
                t.TextToTimeMap.Map(i).Should().BeApproximately(i, 0.01);
        }
    }

    public void Dump(Transcript transcript)
        => Out.WriteLine($"* {transcript}");
    public void Dump(TranscriptUpdate transcriptUpdate)
        => Out.WriteLine($"+ {transcriptUpdate}");
}
