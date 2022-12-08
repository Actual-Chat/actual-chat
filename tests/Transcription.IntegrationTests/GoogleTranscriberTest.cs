using ActualChat.Audio;
using ActualChat.Hosting;
using ActualChat.Module;
using ActualChat.Transcription.Google;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Stl.IO;

namespace ActualChat.Transcription.IntegrationTests;

public class GoogleTranscriberTest : TestBase
{
    private ILogger<GoogleTranscriber> Log { get; }
    private CoreSettings CoreSettings { get; }

    public GoogleTranscriberTest(IConfiguration configuration, ITestOutputHelper @out, ILogger<GoogleTranscriber> log) : base(@out)
    {
        Log = log;
        CoreSettings = configuration.GetSettings<CoreSettings>();
    }

    [Theory]
    [InlineData("file.webm")]
    // [InlineData("large-file.webm")]
    public async Task TranscribeTest(string fileName)
    {
        // Global - Google Speech v2 doesnt work with Http/3!
        // GlobalHttpSettings.SocketsHttpHandler.AllowHttp3
        // TODO(AK): try to disable Http/3 for google speech-to-text only instead of global toggle!
        AppContext.SetSwitch("System.Net.SocketsHttpHandler.Http3Support", false);
        var transcriber = new GoogleTranscriber(
            CoreSettings,
            new MemoryCache(Options.Create(new MemoryCacheOptions())));
        var options = new TranscriptionOptions {
            Language = new ("ru-RU"),
            IsDiarizationEnabled = false,
            IsPunctuationEnabled = true,
            MaxSpeakerCount = 1,
        };
        var audio = await GetAudio(fileName);
        var diffs = await transcriber.Transcribe("test-user", options, audio, default).ToListAsync();

        foreach (var diff in diffs)
            Out.WriteLine(diff.ToString());
        var transcript = diffs.ApplyDiffs().Last();
        Out.WriteLine(transcript.ToString());
    }

    [Fact(Skip = "Manual")]
    public async Task ProperTextMapTest()
    {
        // TODO(AK): try to disable Http/3 for google speech-to-text only instead of global toggle!
        AppContext.SetSwitch("System.Net.SocketsHttpHandler.Http3Support", false);
        var fileName = "0000-AY.webm";
        var transcriber = new GoogleTranscriber(
            CoreSettings,
            new MemoryCache(Options.Create(new MemoryCacheOptions())));
        var options = new TranscriptionOptions() {
            Language = new ("ru-RU"),
            IsDiarizationEnabled = false,
            IsPunctuationEnabled = true,
            MaxSpeakerCount = 1,
        };
        var audio = await GetAudio(fileName);
        var diffs = await transcriber.Transcribe("test-user", options, audio, default).ToListAsync();

        foreach (var diff in diffs)
            Out.WriteLine(diff.ToString());
        var transcript = diffs.ApplyDiffs().Last();
        Out.WriteLine(transcript.ToString());
    }

    private async Task<AudioSource> GetAudio(FilePath fileName, bool webMStream = true)
    {
        var byteStream = GetAudioFilePath(fileName).ReadByteStream(1024, CancellationToken.None);
        var streamAdapter = webMStream
            ? (IAudioStreamAdapter)new WebMStreamAdapter(Log)
            : new ActualOpusStreamAdapter(Log);
        var audio = await streamAdapter.Read(byteStream, CancellationToken.None);
        await audio.WhenFormatAvailable.ConfigureAwait(false);
        return audio;
    }

    private static FilePath GetAudioFilePath(FilePath fileName)
        => new FilePath(Environment.CurrentDirectory) & "data" & fileName;
}
