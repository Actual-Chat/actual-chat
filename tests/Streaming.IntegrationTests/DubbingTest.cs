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
    public async Task GetAudioShouldReturnNullWhenTheSourceIsAlreadyInTheTargetLanguage()
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

        // assert
        stream.Should().BeNull("an English speaker isn't dubbed into English");

        source.Writer.Complete();
        translated.Writer.Complete();
        await pushSourceTask.SilentAwait(false);
        await pushTranslatedTask.SilentAwait(false);
    }

    [Fact(Timeout = 60_000)]
    public async Task GetAudioShouldRetryAfterANoTranscriptMiss()
    {
        // arrange
        var services = AppHost.Services;
        var backend = services.GetRequiredService<IAudioStreamingBackend>();
        var sourceId = StreamId.New(services.MeshWatcher().ThisNode.Ref);
        var dubId = StreamId.New(sourceId, Languages.Russian);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(50));
        var ct = cts.Token;

        // act - nothing is pushed yet, so the dub worker misses the source transcript
        var missedStream = await backend.GetAudio(dubId, TimeSpan.Zero, ct);

        // assert
        missedStream.Should().BeNull("there is no transcript to dub yet");

        // arrange
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
        stream.Should().NotBeNull("a miss must not be remembered as a decision");
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

    // Private methods

    private static Transcript Stable(string text, params Language[] languages)
        => new(text, LinearMap.Zero.Append(new Vector2(text.Length, text.Length)), languages) { IsStable = true };
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
