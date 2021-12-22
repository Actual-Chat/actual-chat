using ActualChat.Audio;
using ActualChat.Transcription.Google;
using Stl.IO;

namespace ActualChat.Transcription.IntegrationTests;

public class GoogleTranscriberTest : TestBase
{
    private ILogger<GoogleTranscriber> Log { get; }

    public GoogleTranscriberTest(ITestOutputHelper @out, ILogger<GoogleTranscriber> log) : base(@out)
        => Log = log;

    [Theory]
    [InlineData("file.webm")]
    // [InlineData("large-file.webm")]
    public async Task TranscribeTest(string fileName)
    {
        var transcriber = new GoogleTranscriber(Log);
        var options = new TranscriptionOptions() {
            Language = "ru-RU",
            IsDiarizationEnabled = false,
            IsPunctuationEnabled = true,
            MaxSpeakerCount = 1,
        };
        var audio = await GetAudio(fileName);
        var diffs = await transcriber.Transcribe(options, audio.GetStream(default), default).ToListAsync();

        foreach (var diff in diffs)
            Out.WriteLine(diff.ToString());
        var transcript = diffs.ApplyDiffs().Last();
        Out.WriteLine(transcript.ToString());
    }

    [Fact(Skip = "Manual")]
    public async Task ProperTextMapTest()
    {
        var fileName = "0000-AY.webm";
        var transcriber = new GoogleTranscriber(Log);
        var options = new TranscriptionOptions() {
            Language = "ru-RU",
            IsDiarizationEnabled = false,
            IsPunctuationEnabled = true,
            MaxSpeakerCount = 1,
        };
        var audio = await GetAudio(fileName);
        var diffs = await transcriber.Transcribe(options, audio.GetStream(default), default).ToListAsync();

        foreach (var diff in diffs)
            Out.WriteLine(diff.ToString());
        var transcript = diffs.ApplyDiffs().Last();
        Out.WriteLine(transcript.ToString());
    }

    private async Task<AudioSource> GetAudio(FilePath fileName, CancellationToken cancellationToken = default)
    {
        var blobStream = GetAudioFilePath(fileName).ReadBlobStream(1024, cancellationToken);
        var audio = new AudioSource(blobStream, new AudioMetadata(), default, null, cancellationToken);
        await audio.WhenFormatAvailable.ConfigureAwait(false);
        return audio;
    }

    private static FilePath GetAudioFilePath(FilePath fileName)
        => new FilePath(Environment.CurrentDirectory) & "data" & fileName;
}
