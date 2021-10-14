using ActualChat.Mathematics;

namespace ActualChat.Transcription.UnitTests;

public class TranscriptUpdateTests
{
    public ITestOutputHelper Out { get; }

    public TranscriptUpdateTests(ITestOutputHelper @out)
        => Out = @out;

    [Fact]
    public void UpdateTranscriptTest()
    {
        var transcript = new Transcript();
        var update = new Transcript
            { Text = "раз", TextToTimeMap = new LinearMap(new[] { 0, 3d }, new[] { 0, 1.86 }) };
        transcript = transcript.WithUpdate(new TranscriptUpdate(update));
        update = new Transcript
            { Text = "рад", TextToTimeMap = new LinearMap(new[] { 0, 3d }, new[] { 0, 1.92 }) };
        transcript = transcript.WithUpdate(new TranscriptUpdate(update));
        update = new Transcript
            { Text = "работа", TextToTimeMap = new LinearMap(new[] { 0, 6d }, new[] { 0, 1.98 }) };
        transcript = transcript.WithUpdate(new TranscriptUpdate(update));
        update = new Transcript
            { Text = "разбор", TextToTimeMap = new LinearMap(new[] { 0, 6d }, new[] { 0, 2.04 }) };
        transcript = transcript.WithUpdate(new TranscriptUpdate(update));
        update = new Transcript
            { Text = "работа", TextToTimeMap = new LinearMap(new[] { 0, 6d }, new[] { 0, 2.22 }) };
        transcript = transcript.WithUpdate(new TranscriptUpdate(update));
        update = new Transcript
            { Text = "раз-два-три", TextToTimeMap = new LinearMap(new[] { 0, 11d }, new[] { 0, 2.28 }) };
        transcript = transcript.WithUpdate(new TranscriptUpdate(update));
        update = new Transcript
            { Text = "раз-два-три-четыре", TextToTimeMap = new LinearMap(new[] { 0, 18d }, new[] { 0, 2.76 }) };
        transcript = transcript.WithUpdate(new TranscriptUpdate(update));
        update = new Transcript
            { Text = "раз-два-три-четыре", TextToTimeMap = new LinearMap(new[] { 0, 18d }, new[] { 0, 3.36 }) };
        transcript = transcript.WithUpdate(new TranscriptUpdate(update));
        update = new Transcript
            { Text = "раз-два-три-четыре-пять", TextToTimeMap = new LinearMap(new[] { 0, 23d }, new[] { 0, 3.42 }) };
        transcript = transcript.WithUpdate(new TranscriptUpdate(update));
        update = new Transcript
            { Text = "раз-два-три-четыре-пять", TextToTimeMap = new LinearMap(new[] { 0, 23d }, new[] { 0, 4.02 }) };
        transcript = transcript.WithUpdate(new TranscriptUpdate(update));
        update = new Transcript {
            Text = "раз-два-три-четыре-пять Вышел",
            TextToTimeMap = new LinearMap(new[] { 23d, 29d }, new[] { 4, 4.92 }),
        };
        transcript = transcript.WithUpdate(new TranscriptUpdate(update));
        update = new Transcript {
            Text = "раз-два-три-четыре-пять Вышел зайчик",
            TextToTimeMap = new LinearMap(new[] { 23d, 36d }, new[] { 4, 5.52 }),
        };
        transcript = transcript.WithUpdate(new TranscriptUpdate(update));
        update = new Transcript {
            Text = "раз-два-три-четыре-пять вышел зайчик погулять",
            TextToTimeMap = new LinearMap(new[] { 23d, 45d }, new[] { 4, 7.44 }),
        };
        transcript = transcript.WithUpdate(new TranscriptUpdate(update));
        update = new Transcript {
            Text = "раз-два-три-четыре-пять вышел зайчик погулять Вдруг откуда",
            TextToTimeMap = new LinearMap(new[] { 23d, 58d }, new[] { 4, 8.34 }),
        };
        transcript = transcript.WithUpdate(new TranscriptUpdate(update));

        Out.WriteLine(transcript.ToString());
    }
}
