using ActualChat.Audio;
using ActualChat.Hosting;
using ActualChat.IO;
using ActualChat.Module;
using ActualChat.Streaming;
using ActualChat.Streaming.Services.Transcribers;
using Microsoft.Extensions.Configuration;
using ActualLab.IO;
using ActualLab.Testing.Output;
using Xunit.DependencyInjection.Logging;

namespace ActualChat.Transcription.IntegrationTests;

[Collection(nameof(TranscriptionCollection))]
public class GoogleTranscriberTest(
    IConfiguration configuration,
    ITestOutputHelper @out,
    ILogger<GoogleTranscriberTest> log
    ) : TestBase(@out, log)
{
    private CoreServerSettings CoreServerSettings { get; }
        = configuration.GetSettings<CoreServerSettings>(nameof(CoreSettings));

    [Theory(Skip = "For manual runs only")]
    [InlineData("file.webm", false)]
    [InlineData("file.webm", true)]
    [InlineData("0002-AK.opuss", true)]
    // [InlineData("0003-AK.opuss", true)] - fails as too short???
    [InlineData("tail-cut.opuss", true)]
    public async Task TranscribeWorks(string fileName, bool withDelay)
    {
        // Global - Google Speech v2 doesnt work with Http/3!
        // GlobalHttpSettings.SocketsHttpHandler.AllowHttp3
        // TODO(AK): try to disable Http/3 for google speech-to-text only instead of global toggle!
        AppContext.SetSwitch("System.Net.SocketsHttpHandler.Http3Support", false);
        var services = CreateServices();
        var transcriber = new GoogleTranscriber(services);
        var options = new TranscriptionOptions {
            Language = "ru-RU",
        };
        var audio = await GetAudio(fileName, withDelay: withDelay);

        // helper to save webm format
        // await using (var outputStream = new FileStream(
        //     Path.Combine(Environment.CurrentDirectory, "data", file-name),
        //     FileMode.OpenOrCreate,
        //     FileAccess.ReadWrite)) {
        //     var converter = new WebMStreamConverter(Log);
        //     var byteStream = converter.ToByteStream(audio, CancellationToken.None);
        //     await foreach (var data in byteStream) {
        //         await outputStream.WriteAsync(data, CancellationToken.None);
        //     }
        //     await outputStream.FlushAsync();
        // };

        // using var writeBufferLease = MemoryPool<byte>.Shared.Rent(100 * 1024);
        // var writeBuffer = writeBufferLease.Memory;

        var transcripts = await transcriber.Transcribe("test", audio, options).ToListAsync();
        foreach (var t in transcripts)
            Out.WriteLine(t.ToString());
    }

    [Fact]
    public async Task ProperTextMapTest()
    {
        var fileName = "0000-AY.webm";
        var services = CreateServices();
        var transcriber = new GoogleTranscriber(services);
        var options = new TranscriptionOptions {
            Language = "ru-RU",
        };
        var audio = await GetAudio(fileName);
        var transcripts = await transcriber.Transcribe("test", audio, options).ToListAsync();
        foreach (var t in transcripts)
            Out.WriteLine(t.ToString());
        transcripts.Last().TimeRange.Start.Should().Be(0);
    }

    private async Task<AudioSource> GetAudio(FilePath fileName, bool? webMStream = null, bool withDelay = false)
    {
        var byteStream = GetAudioFilePath(fileName).ReadByteStream(1024, CancellationToken.None);
        var isWebMStream = webMStream ?? fileName.Extension == ".webm";
        var converter = isWebMStream
            ? (IAudioStreamConverter)new WebMStreamConverter(MomentClockSet.Default, Log)
            : new ActualOpusStreamConverter(MomentClockSet.Default, Log);
        var audio = await converter.FromByteStream(byteStream, CancellationToken.None);
        if (!withDelay)
            return audio;

        var delayedFrames = audio.GetFrames(CancellationToken.None)
            .SelectAwait(async f => {
                await Task.Delay(20);
                return f;
            });
        var delayedAudio = new AudioSource(
            MomentClockSet.Default.SystemClock.Now,
            audio.Format,
            delayedFrames,
            TimeSpan.Zero,
            Log,
            CancellationToken.None);

        return delayedAudio;
    }

    private static FilePath GetAudioFilePath(FilePath fileName)
        => new FilePath(Environment.CurrentDirectory) & "data" & fileName;

    private IServiceProvider CreateServices()
        => new ServiceCollection()
            .AddSingleton(CoreServerSettings)
            .AddSingleton(MomentClockSet.Default)
            .AddLogging(logging => {
                logging.ClearProviders();
                logging.SetMinimumLevel(LogLevel.Debug);
                logging.AddDebug();
                // XUnit logging requires weird setup b/c otherwise it filters out
                // everything below LogLevel.Information
                logging.AddProvider(
#pragma warning disable CS0618
                    new XunitTestOutputLoggerProvider(
                        new TestOutputHelperAccessor(Out),
                        (_, _) => true));
#pragma warning restore CS0618
            })
            .BuildServiceProvider();
}
