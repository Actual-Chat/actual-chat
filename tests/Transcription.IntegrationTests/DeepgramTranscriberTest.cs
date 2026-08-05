using ActualChat.Hosting;
using ActualChat.Module;
using ActualChat.Streaming;
using ActualChat.Streaming.Module;
using ActualChat.Transcription;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.EnvironmentVariables;

namespace ActualChat.Transcription.IntegrationTests;

[Collection(nameof(TranscriptionCollection))]
public class DeepgramTranscriberTest(ITestOutputHelper @out, ILogger<DeepgramTranscriberTest> log)
    : TranscriberTestBase(@out, log)
{
    [Theory(Skip = "For manual runs only")]
    [InlineData("0004-AK.webm", true)]
    // [InlineData("0001-AY.opuss", true)]
    // [InlineData("0002-AY.opuss", true)]
    // [InlineData("0003-AK.opuss", true)] //- fails as too short???
    // [InlineData("tail-cut.opuss", true)]
    public async Task TranscribeWorks(string fileName, bool withDelay)
    {
        var services = CreateServices();
        var transcriber = new DeepgramTranscriber(services);
        var options = new TranscriptionOptions {
            Language = Language.Parse("ru-RU"),
        };
        var audio = await GetAudio(fileName, withDelay: withDelay);

        // helper to save webm format
        // await using (var outputStream = new FileStream(
        //     Path.Combine(Environment.CurrentDirectory, "data", "tail-cut.webm"),
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
            WriteLine(t.ToString());
    }

    private IServiceProvider CreateServices()
    {
        IConfiguration configuration = new ConfigurationManager {
            Sources = { new EnvironmentVariablesConfigurationSource() },
        };
        return new ServiceCollection()
            .AddSingleton(_ => configuration)
            .AddSingleton(MomentClockSet.Default)
            .AddSingleton(_ => configuration.Settings<CoreServerSettings>(nameof(CoreSettings)))
            .AddSingleton<StreamingSettings>(c => new StreamingServiceModule(c).Settings)
            .AddTestLogging(Out)
            .BuildServiceProvider();
    }
}
