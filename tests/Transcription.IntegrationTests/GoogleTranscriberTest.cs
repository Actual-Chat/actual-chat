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

    [Theory(Skip = "Manual")]
    [InlineData("file.webm", false)]
    [InlineData("file.webm", true)]
    [InlineData("0002-AK.opuss", true)]
    // [InlineData("0003-AK.opuss", true)] - fails as too short???
    public async Task TranscribeWorks(string fileName, bool withDelay)
    {
        // Global - Google Speech v2 doesnt work with Http/3!
        // GlobalHttpSettings.SocketsHttpHandler.AllowHttp3
        // TODO(AK): try to disable Http/3 for google speech-to-text only instead of global toggle!
        AppContext.SetSwitch("System.Net.SocketsHttpHandler.Http3Support", false);
        var transcriber = new GoogleTranscriber(
            CoreSettings,
            new MemoryCache(Options.Create(new MemoryCacheOptions())),
            Log);
        var options = new TranscriptionOptions {
            Language = new ("ru-RU"),
            IsDiarizationEnabled = false,
            IsPunctuationEnabled = true,
            MaxSpeakerCount = 1,
        };
        var audio = await GetAudio(fileName, withDelay: withDelay);

        // helper to save webm format
        // await using (var outputStream = new FileStream(
        //     Path.Combine(Environment.CurrentDirectory, "data", file-name),
        //     FileMode.OpenOrCreate,
        //     FileAccess.ReadWrite)) {
        //     var webMAdapter = new WebMStreamAdapter(Log);
        //     var byteStream = webMAdapter.Write(audio, CancellationToken.None);
        //     await foreach (var data in byteStream) {
        //         await outputStream.WriteAsync(data, CancellationToken.None);
        //     }
        //     await outputStream.FlushAsync();
        // };

        // using var writeBufferLease = MemoryPool<byte>.Shared.Rent(100 * 1024);
        // var writeBuffer = writeBufferLease.Memory;

        var diffs = await transcriber.Transcribe("dev-tst", "test", audio, options, default).ToListAsync();

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
            new MemoryCache(Options.Create(new MemoryCacheOptions())),
            Log);
        var options = new TranscriptionOptions {
            Language = new ("ru-RU"),
            IsDiarizationEnabled = false,
            IsPunctuationEnabled = true,
            MaxSpeakerCount = 1,
        };
        var audio = await GetAudio(fileName);
        var diffs = await transcriber.Transcribe("dev-tst", "test", audio, options, default).ToListAsync();

        foreach (var diff in diffs)
            Out.WriteLine(diff.ToString());
        var transcript = diffs.ApplyDiffs().Last();
        Out.WriteLine(transcript.ToString());
    }

    private async Task<AudioSource> GetAudio(FilePath fileName, bool? webMStream = null, bool withDelay = false)
    {
        var byteStream = GetAudioFilePath(fileName).ReadByteStream(1024, CancellationToken.None);
        var isWebMStream = webMStream ?? fileName.Extension == ".webm";
        var streamAdapter = isWebMStream
            ? (IAudioStreamAdapter)new WebMStreamAdapter(Log)
            : new ActualOpusStreamAdapter(Log);
        var audio = await streamAdapter.Read(byteStream, CancellationToken.None);
        await audio.WhenFormatAvailable.ConfigureAwait(false);
        if (!withDelay)
            return audio;

        var delayedFrames = audio.GetFrames(CancellationToken.None)
            .SelectAwait(async f => {
                await Task.Delay(20);
                return f;
            });
        var delayedAudio = new AudioSource(Task.FromResult(audio.Format),
            delayedFrames,
            TimeSpan.Zero,
            Log,
            CancellationToken.None);

        return delayedAudio;
    }

    private static FilePath GetAudioFilePath(FilePath fileName)
        => new FilePath(Environment.CurrentDirectory) & "data" & fileName;
}
