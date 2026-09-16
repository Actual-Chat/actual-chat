using System.Numerics;
using ActualChat.Audio;
using ActualChat.Testing.Host;
using ActualChat.Transcription;
using ActualLab.Rpc;

namespace ActualChat.Streaming.IntegrationTests;

[Collection(nameof(DubbingCollection))]
public class DubbingTest(DubbingCollection.AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<DubbingCollection.AppHostFixture>(fixture, @out)
{
    [Fact(Timeout = 60_000)]
    public async Task GetAudioShouldPublishADubForATranslatedTranscript()
    {
        // arrange
        var services = AppHost.Services;
        var backend = services.GetRequiredService<IAudioStreamingBackend>();
        var sourceId = StreamId.New(services.MeshWatcher().ThisNode.Ref);
        var dubId = StreamId.New(sourceId, Languages.Russian);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        var ct = cts.Token;
        var source = Channel.CreateUnbounded<TranscriptDiff>();
        var translated = Channel.CreateUnbounded<TranscriptDiff>();
        var pushSourceTask = BackgroundTask.Run(
            () => backend.PushTranscript(sourceId, new RpcStream<TranscriptDiff>(source.Reader.ReadAllAsync(ct)), ct),
            ct);
        var pushTranslatedTask = BackgroundTask.Run(
            () => backend.PushTranscript(dubId, new RpcStream<TranscriptDiff>(translated.Reader.ReadAllAsync(ct)), ct),
            ct);
        source.Writer.TryWrite(Stable("Hello there, how are you doing today?") - Transcript.Empty);
        translated.Writer.TryWrite(Stable("Привет, как у тебя сегодня дела?") - Transcript.Empty);
        await backend.WhenTranscriptPublished(dubId, ct);

        // act
        var stream = await backend.GetAudio(dubId, TimeSpan.Zero, ct);

        // assert
        stream.Should().NotBeNull("the fake synthesizer dubs any translated text");
        var frames = new List<AudioFrame>();
        await foreach (var frame in stream!.WithCancellation(ct)) {
            frames.Add(frame);
            if (frames.Count >= 3)
                break;
        }
        frames[0].Offset.Should().Be(TimeSpan.FromMilliseconds(-1), "the first frame is the stream header");
        frames.Skip(1).Select(f => f.Offset).Should().Equal(TimeSpan.Zero, Constants.Audio.OpusFrameDuration);

        source.Writer.Complete();
        translated.Writer.Complete();
        await pushSourceTask.SilentAwait(false);
        await pushTranslatedTask.SilentAwait(false);
    }

    [Fact(Timeout = 60_000)]
    public async Task GetAudioShouldServeTheOriginalAloneWhenTheSourceIsAlreadyInTheTargetLanguage()
    {
        // arrange
        var services = AppHost.Services;
        var backend = services.GetRequiredService<IAudioStreamingBackend>();
        var sourceId = StreamId.New(services.MeshWatcher().ThisNode.Ref);
        var dubId = StreamId.New(sourceId, Languages.English);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        var ct = cts.Token;
        var source = Channel.CreateUnbounded<TranscriptDiff>();
        var translated = Channel.CreateUnbounded<TranscriptDiff>();
        var pushSourceTask = BackgroundTask.Run(
            () => backend.PushTranscript(sourceId, new RpcStream<TranscriptDiff>(source.Reader.ReadAllAsync(ct)), ct),
            ct);
        var pushTranslatedTask = BackgroundTask.Run(
            () => backend.PushTranscript(dubId, new RpcStream<TranscriptDiff>(translated.Reader.ReadAllAsync(ct)), ct),
            ct);
        var text = Stable("Hello there, how are you doing today?", Languages.English);
        source.Writer.TryWrite(text - Transcript.Empty);
        translated.Writer.TryWrite(text - Transcript.Empty);
        await backend.WhenTranscriptPublished(dubId, ct);

        // act
        var stream = await backend.GetAudio(dubId, TimeSpan.Zero, ct);
        source.Writer.Complete();
        translated.Writer.Complete();
        await pushSourceTask.SilentAwait(false);
        await pushTranslatedTask.SilentAwait(false);
        var frames = await stream!.ToListAsync(ct);

        // assert - the mix is served regardless and ends with the (absent) original: header only
        frames.Select(f => f.Offset).Should().Equal([TimeSpan.FromMilliseconds(-1)],
            "an English speaker isn't dubbed into English, so nothing is summed onto the original");
    }

    [Fact(Timeout = 60_000)]
    public async Task GetAudioShouldEndTheMixWithTheOriginalAfterANoTranscriptMiss()
    {
        // arrange
        var services = AppHost.Services;
        var backend = services.GetRequiredService<IAudioStreamingBackend>();
        var sourceId = StreamId.New(services.MeshWatcher().ThisNode.Ref);
        var dubId = StreamId.New(sourceId, Languages.Russian);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(50));
        var ct = cts.Token;

        // act - nothing is pushed at all, so the dub worker misses the source transcript
        var startedAt = CpuTimestamp.Now;
        var stream = await backend.GetAudio(dubId, TimeSpan.Zero, ct);
        var frames = await stream!.ToListAsync(ct);

        // assert - the audio share wait, the transcript wait and its one grace pass, then the end
        frames.Select(f => f.Offset).Should().Equal([TimeSpan.FromMilliseconds(-1)],
            "with no transcript nothing is dubbed, and with no audio the mix is the header alone");
        startedAt.Elapsed.Should().BeLessThan(TimeSpan.FromSeconds(10), "a miss ends the mix, it doesn't hold it");
    }

    [Fact(Timeout = 60_000)]
    public async Task ADubThatNeverGetsStableTextShouldEndWithTheOriginal()
    {
        // arrange
        var services = AppHost.Services;
        var backend = services.GetRequiredService<IAudioStreamingBackend>();
        var sourceId = StreamId.New(services.MeshWatcher().ThisNode.Ref);
        var dubId = StreamId.New(sourceId, Languages.English);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        var ct = cts.Token;
        var source = Channel.CreateUnbounded<TranscriptDiff>();
        var translated = Channel.CreateUnbounded<TranscriptDiff>();
        var pushSourceTask = BackgroundTask.Run(
            () => backend.PushTranscript(sourceId, new RpcStream<TranscriptDiff>(source.Reader.ReadAllAsync(ct)), ct),
            ct);
        var pushTranslatedTask = BackgroundTask.Run(
            () => backend.PushTranscript(dubId, new RpcStream<TranscriptDiff>(translated.Reader.ReadAllAsync(ct)), ct),
            ct);
        // The detected language decides "dub" before any translation is stable...
        source.Writer.TryWrite(Stable("Привет, как у тебя сегодня дела?", Languages.Russian) - Transcript.Empty);
        translated.Writer.TryWrite(Unstable("Hello, how are") - Transcript.Empty);
        await backend.WhenTranscriptPublished(dubId, ct);

        // act
        var stream = await backend.GetAudio(dubId, TimeSpan.Zero, ct);
        // ...and then the translation ends without ever becoming stable
        source.Writer.Complete();
        translated.Writer.Complete();
        await pushSourceTask.SilentAwait(false);
        await pushTranslatedTask.SilentAwait(false);

        // assert
        stream.Should().NotBeNull("the language branch decided to dub");
        var frames = await stream!.ToListAsync(ct);
        frames.Select(f => f.Offset).Should().Equal([TimeSpan.FromMilliseconds(-1)],
            "a dub with nothing to say ends the mix cleanly with the original - here, the header alone");

        // A silent translation is not a synthesizer outage: the next dub must still be spoken
        var sourceId2 = StreamId.New(services.MeshWatcher().ThisNode.Ref);
        var dubId2 = StreamId.New(sourceId2, Languages.Russian);
        var source2 = Channel.CreateUnbounded<TranscriptDiff>();
        var translated2 = Channel.CreateUnbounded<TranscriptDiff>();
        var pushSource2Task = BackgroundTask.Run(
            () => backend.PushTranscript(sourceId2, new RpcStream<TranscriptDiff>(source2.Reader.ReadAllAsync(ct)), ct),
            ct);
        var pushTranslated2Task = BackgroundTask.Run(
            () => backend.PushTranscript(dubId2, new RpcStream<TranscriptDiff>(translated2.Reader.ReadAllAsync(ct)), ct),
            ct);
        source2.Writer.TryWrite(Stable("Hello there, how are you doing today?") - Transcript.Empty);
        translated2.Writer.TryWrite(Stable("Привет, как у тебя сегодня дела?") - Transcript.Empty);
        await backend.WhenTranscriptPublished(dubId2, ct);
        var stream2 = await backend.GetAudio(dubId2, TimeSpan.Zero, ct);
        source2.Writer.Complete();
        translated2.Writer.Complete();
        await pushSource2Task.SilentAwait(false);
        await pushTranslated2Task.SilentAwait(false);
        var frames2 = await stream2!.ToListAsync(ct);
        frames2.Count(f => f.Offset >= TimeSpan.Zero).Should().BeGreaterThan(0,
            "only a synthesizer failure trips the provider-down cool-down");
    }

    // Private methods

    private static Transcript Stable(string text, params Language[] languages)
        => Unstable(text, languages) with { IsStable = true };

    // One second of audio, whatever the length: a text-length time map would read as a long
    // backlog and send every dub down the late-listener path
    private static Transcript Unstable(string text, params Language[] languages)
        => new(text, LinearMap.Zero.Append(new Vector2(text.Length, 1)), languages);
}

[CollectionDefinition(nameof(DubbingCollection))]
public sealed class DubbingCollection : ICollectionFixture<DubbingCollection.AppHostFixture>
{
    public sealed class AppHostFixture(IMessageSink messageSink)
        : ActualChat.Testing.Host.AppHostFixture(
            "dubbing",
            messageSink,
            TestAppHostOptions.Default with {
                ConfigureServices = (_, services) => {
                    services.AddSingleton<ISpeechSynthesizer>(c => new FakeSpeechSynthesizer(c));
                },
            });
}
