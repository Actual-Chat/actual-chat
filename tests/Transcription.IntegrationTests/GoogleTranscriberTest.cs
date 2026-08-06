using ActualChat.Module;
using Microsoft.Extensions.Configuration;

namespace ActualChat.Transcription.IntegrationTests;

[Collection(nameof(TranscriptionCollection))]
public class GoogleTranscriberTest(
    IConfiguration configuration,
    ITestOutputHelper @out,
    ILogger<GoogleTranscriberTest> log
    ) : TranscriberTestBase(@out, log)
{
    private CoreServerSettings CoreServerSettings { get; }
        = configuration.Settings<CoreServerSettings>(nameof(CoreSettings));

    [Theory(Skip = "For manual runs only")]
    // [Theory]
    // [InlineData("3.webm", true)]
    // [InlineData("file.webm", false)]
    // [InlineData("file.webm", true)]
    // [InlineData("0002-AK.opuss", true)]
    // [InlineData("0003-AK.opuss", true)] - fails as too short???
    // [InlineData("tail-cut.opuss", true)]
    [InlineData("truncated.opuss", true)]
    public async Task TranscribeWorks(string fileName, bool withDelay)
    {
        // Global - Google Speech v2 doesnt work with Http/3!
        // GlobalHttpSettings.SocketsHttpHandler.AllowHttp3
        // TODO(AK): try to disable Http/3 for google speech-to-text only instead of global toggle!
        AppContext.SetSwitch("System.Net.SocketsHttpHandler.Http3Support", false);
        var services = CreateServices();
        var transcriber = new GoogleTranscriber(services);
        var options = new TranscriptionOptions {
            Language = Language.Parse("ru-RU"),
        };
        var audio = await GetAudio(fileName, withDelay: withDelay);

        // helper to save webm format
        // await using (var outputStream = new FileStream(
        //     Path.Combine(Environment.CurrentDirectory, "data", "fileName.webm"),
        //     FileMode.OpenOrCreate,
        //     FileAccess.ReadWrite)) {
        //     var converter = new WebMStreamConverter(MomentClockSet.Default, Log);
        //     var byteStream = converter.ToByteStream(audio, CancellationToken.None);
        //     await foreach (var data in byteStream) {
        //         await outputStream.WriteAsync(data, CancellationToken.None);
        //     }
        //     await outputStream.FlushAsync();
        // };

        // using var writeBufferLease = ArrayPools.SharedBytePool.Lease(100 * 1024);
        // var writeBuffer = writeBufferLease.Memory;

        var transcripts = await transcriber.Transcribe("test", audio, options).ToListAsync();
        foreach (var t in transcripts)
            WriteLine($"{t.Languages.ToDelimitedString()}: {t}");
    }

    [Fact(Skip = "Depends on Google API")]
    // [Fact]
    public async Task ProperTextMapTest()
    {
        var fileName = "0000-AY.webm";
        var services = CreateServices();
        var transcriber = new GoogleTranscriber(services);
        var options = new TranscriptionOptions {
            Language = Language.Parse("ru-RU"),
        };
        var audio = await GetAudio(fileName);
        var transcripts = await transcriber.Transcribe("test", audio, options).ToListAsync();
        foreach (var t in transcripts)
            WriteLine(t.ToString());
        transcripts.Last().TimeRange.Start.Should().Be(0);
    }

    private IServiceProvider CreateServices()
        => new ServiceCollection()
            .AddSingleton(CoreServerSettings)
            .AddSingleton(MomentClockSet.Default)
            .AddTestLogging(Out)
            .BuildServiceProvider();
}
