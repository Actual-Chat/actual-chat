using ActualChat.Audio;
using ActualChat.Module;
using ActualChat.Transcription.Module;
using ActualLab.Generators;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Configuration.EnvironmentVariables;

namespace ActualChat.Transcription.IntegrationTests;

[Collection(nameof(TranscriptionCollection))]
public sealed class SonioxVoicesClientTest(ITestOutputHelper @out, ILogger<SonioxVoicesClientTest> log)
    : TranscriberTestBase(@out, log)
{
    private static readonly TimeSpan ReadyTimeout = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan PollPeriod = TimeSpan.FromMilliseconds(500);

    // Creates a real voice against the org's 20-voice quota with the shared dev key.
    [Fact(Timeout = 120_000, Skip = "For manual runs only")]
    public async Task CreateShouldCloneAUsableVoiceAndDeleteShouldRemoveIt()
    {
        // arrange
        var services = CreateServices();
        if (services.GetRequiredService<CoreServerSettings>().SonioxKey.IsNullOrEmpty()) {
            WriteLine("CoreSettings__SonioxKey is not set - skipping.");
            return;
        }

        var voices = services.GetRequiredService<ISonioxVoices>();
        using var wav = await BuildSampleWav();
        var name = $"voxt-test-{RandomStringGenerator.Default.Next()}";
        string? voiceId = null;
        try {
            // act
            var created = await voices.Create(name, wav, CancellationToken.None);
            voiceId = created.Id;
            WriteLine($"Created voice {created.Id}, IsReady={created.IsReady}, IsFailed={created.IsFailed}");

            var ready = await PollUntilReady(voices, created.Id);

            // assert - the clone is usable
            ready.Should().NotBeNull("the voice must reach a terminal per-model status within the timeout");
            ready!.IsFailed.Should().BeFalse();
            ready.IsReady.Should().BeTrue();

            var listed = await voices.List(CancellationToken.None);
            listed.Should().Contain(v => v.Id == created.Id);

            var pcm = Channel.CreateUnbounded<byte[]>();
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await new SonioxTtsClient(services).Generate("en", created.Id, "Hello", pcm.Writer, cts.Token);
            var byteCount = 0;
            await foreach (var chunk in pcm.Reader.ReadAllAsync(cts.Token))
                byteCount += chunk.Length;
            var seconds = byteCount / (double)(Constants.Audio.PlaybackSampleRate * sizeof(short));
            WriteLine($"Synthesized {byteCount} bytes = {seconds:F2}s with the clone");
            seconds.Should().BeGreaterThan(0.1, "the clone must speak an audible amount of audio");
        }
        finally {
            if (voiceId != null)
                await voices.Delete(voiceId, CancellationToken.None);
        }

        // assert - gone after delete
        (await voices.Get(voiceId!, CancellationToken.None)).Should().BeNull();
    }

    // Private methods

    private IServiceProvider CreateServices()
    {
        IConfiguration configuration = new ConfigurationManager {
            Sources = { new EnvironmentVariablesConfigurationSource() },
        };
        return new ServiceCollection()
            .AddSingleton<IConfiguration>(_ => configuration)
            .AddSingleton(MomentClockSet.Default)
            .AddSingleton(_ => configuration.Settings<CoreServerSettings>(nameof(CoreSettings)))
            .AddSingleton(new TranscriptionSettings())
            .AddSoniox()
            .AddTestLogging(Out)
            .BuildServiceProvider();
    }

    private async Task<MemoryStream> BuildSampleWav()
    {
        var audio = await GetAudio("long-ru-en-1.webm");
        using var decoder = new OpusToPcmDecoder();
        using var pcm = new MemoryStream();
        var maxByteCount = 60 * Constants.Audio.RecordingSampleRate * sizeof(short);
        await foreach (var frame in audio.GetFrames(CancellationToken.None)) {
            if (pcm.Length >= maxByteCount)
                break;

            pcm.Write(decoder.Decode(frame.Data.Span));
        }

        var wav = new MemoryStream();
        WavWriter.Write(wav, pcm.GetBuffer().AsSpan(0, (int)pcm.Length), Constants.Audio.RecordingSampleRate);
        wav.Position = 0;
        return wav;
    }

    private async Task<SonioxVoice?> PollUntilReady(ISonioxVoices voices, string id)
    {
        var deadline = DateTime.UtcNow + ReadyTimeout;
        while (DateTime.UtcNow < deadline) {
            var voice = await voices.Get(id, CancellationToken.None);
            if (voice is { IsReady: true } or { IsFailed: true })
                return voice;

            await Task.Delay(PollPeriod);
        }
        return await voices.Get(id, CancellationToken.None);
    }
}
