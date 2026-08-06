using ActualChat.Module;
using Microsoft.Extensions.Configuration;
using EndpointingSensitivity = Google.Cloud.Speech.V2.StreamingRecognitionFeatures.Types.EndpointingSensitivity;

namespace ActualChat.Transcription.IntegrationTests;

// Diagnostic: replays a real recording through Google and reports the audio position each update
// covers, which is what tells apart "streams continuously" from "emits a segment per pause".

[Collection(nameof(TranscriptionCollection))]
public sealed class GoogleRealtimeTest(
    IConfiguration configuration,
    ITestOutputHelper @out,
    ILogger<GoogleRealtimeTest> log
    ) : TranscriberTestBase(@out, log)
{
    private const string Clip = "long-ru-en-1.webm";
    private CoreServerSettings CoreServerSettings { get; }
        = configuration.Settings<CoreServerSettings>(nameof(CoreSettings));

    [Theory(Skip = "Diagnostic, for manual runs only")]
    [InlineData("chirp_2", EndpointingSensitivity.Standard)]
    [InlineData("chirp_2", EndpointingSensitivity.Short)]
    [InlineData("chirp_2", EndpointingSensitivity.Supershort)]
    public async Task MeasureUpdateGranularity(string model, EndpointingSensitivity sensitivity)
    {
        // arrange
        // Google Speech v2 doesn't work over HTTP/3.
        AppContext.SetSwitch("System.Net.SocketsHttpHandler.Http3Support", false);
        var services = new ServiceCollection()
            .AddSingleton<IConfiguration>(_ => configuration)
            .AddSingleton(MomentClockSet.Default)
            .AddSingleton(_ => CoreServerSettings)
            .AddTestLogging(Out)
            .BuildServiceProvider();
        var transcriberOptions = new GoogleTranscriber.Options {
            Model = model,
            EndpointingSensitivity = sensitivity,
        };
        var transcriber = new GoogleTranscriber(services, transcriberOptions);
        var options = new TranscriptionOptions { Language = Language.Parse("ru-RU") };
        // GoogleTranscriber paces the audio itself, so the source must not be throttled twice.
        var audio = await GetAudio(Clip, withDelay: false);

        // act
        var startedAt = CpuTimestamp.Now;
        var updates = new List<(TimeSpan At, float AudioAt, int Length)>();
        await foreach (var transcript in transcriber.Transcribe("test", audio, options)) {
            var audioAt = transcript.TimeMap.IsEmpty ? 0f : transcript.TimeMap.YRange.End;
            updates.Add((startedAt.Elapsed, audioAt, transcript.Text.Length));
        }

        // assert
        WriteLine($"=== {model}, {sensitivity}");
        WriteLine($"{updates.Count} updates over {startedAt.Elapsed.TotalSeconds:F1}s");
        WriteLine("at(s) | audioAt(s) | step(s) | chars");
        var lastAudioAt = 0f;
        foreach (var (at, audioAt, length) in updates) {
            WriteLine($"{at.TotalSeconds,5:F1} | {audioAt,10:F1} | {audioAt - lastAudioAt,7:F1} | {length,5}");
            lastAudioAt = audioAt;
        }

        var steps = updates.Select(x => x.AudioAt).Zip(updates.Skip(1).Select(x => x.AudioAt), (a, b) => b - a)
            .Where(x => x > 0)
            .ToList();
        if (steps.Count != 0)
            WriteLine($"\nAudio step per update: avg {steps.Average():F2}s, max {steps.Max():F2}s");

        updates.Should().NotBeEmpty();
    }
}
