namespace ActualChat.Transcription.UnitTests;

public class TranscriptUpdateTests : TestBase
{
    public TranscriptUpdateTests(ITestOutputHelper @out) : base(@out) { }

    [Fact]
    public void UpdateTranscriptTest()
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
}
