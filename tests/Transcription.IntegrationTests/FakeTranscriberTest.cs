using ActualChat.Audio;
using ActualChat.IO;
using ActualChat.Streaming;
using ActualChat.Streaming.Services.Transcribers;
using ActualLab.IO;

namespace ActualChat.Transcription.IntegrationTests;

[Collection(nameof(TranscriptionCollection))]
public class FakeTranscriberTest(ITestOutputHelper @out, ILogger<FakeTranscriberTest> log)
    : TestBase(@out, log)
{
    private const double WordsPerSecond = 4.0;
    private const string ExpectedPrefix = "This is your fake transcription.";

    [Theory]
    [InlineData("large-file.webm")]
    public async Task TranscribeProducesPacedFakeText(string fileName)
    {
        // arrange
        var services = CreateServices();
        var transcriber = new FakeTranscriber(services);
        var options = new TranscriptionOptions {
            Language = Languages.English,
        };
        var audio = await GetAudio(fileName);

        // act
        var transcripts = await transcriber.Transcribe("test", audio, options).ToListAsync();
        foreach (var t in transcripts)
            WriteLine(t.ToString());

        // assert
        transcripts.Should().NotBeEmpty();
        var final = transcripts[^1];
        final.IsStable.Should().BeTrue();
        final.Text.Should().StartWith(ExpectedPrefix);
        final.Languages.Should().Equal(Languages.English);

        var audioSeconds = audio.Duration.TotalSeconds;
        audioSeconds.Should().BeGreaterThan(2,
            "the test relies on a sufficiently long audio sample");

        var expectedWordCount = (int)Math.Ceiling(audioSeconds * WordsPerSecond);
        var actualWordCount = final.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;
        actualWordCount.Should().Be(expectedWordCount);

        final.TimeRange.End.Should().BeApproximately((float)audioSeconds, 0.01f);

        // Intermediate transcripts should be unstable and grow monotonically in length
        var lastLen = 0;
        for (var i = 0; i < transcripts.Count - 1; i++) {
            transcripts[i].IsStable.Should().BeFalse();
            transcripts[i].Length.Should().BeGreaterThanOrEqualTo(lastLen);
            lastLen = transcripts[i].Length;
        }
    }

    private async Task<AudioSource> GetAudio(FilePath fileName)
    {
        var byteStream = GetAudioFilePath(fileName).ReadByteStream(1024, CancellationToken.None);
        var isWebMStream = fileName.Extension == ".webm";
        var converter = isWebMStream
            ? (IAudioStreamConverter)new WebMStreamConverter(MomentClockSet.Default, Log)
            : new ActualOpusStreamConverter(MomentClockSet.Default, Log);
        return await converter.FromByteStream(byteStream, CancellationToken.None);
    }

    private static FilePath GetAudioFilePath(FilePath fileName)
        => new FilePath(Environment.CurrentDirectory) & "data" & fileName;

    private IServiceProvider CreateServices()
        => new ServiceCollection()
            .AddSingleton(MomentClockSet.Default)
            .AddTestLogging(Out)
            .BuildServiceProvider();
}
