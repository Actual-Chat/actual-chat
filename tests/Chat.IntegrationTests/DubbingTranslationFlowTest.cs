using System.Numerics;
using ActualChat.Chat.Module;
using ActualChat.Module;
using ActualChat.Streaming;
using ActualChat.Testing.Host;
using ActualChat.Transcription;
using ActualLab.Rpc;

namespace ActualChat.Chat.IntegrationTests;

// Drives the dub off the real TranslationsBackend stream: the source transcript is pushed with
// Soniox-like diffs (unstable growth, one stable diff at the end) and the text entry the
// translation is keyed by is created after the transcript is published, as ProcessAudio does.

[Collection(nameof(DubbingTranslationCollection))]
public class DubbingTranslationFlowTest(
    DubbingTranslationCollection.AppHostFixture fixture,
    ITestOutputHelper @out)
    : SharedAppHostTestBase<DubbingTranslationCollection.AppHostFixture>(fixture, @out)
{
    private const string SourceText = "Привет, как у тебя сегодня дела?";
    private static readonly string[] SourceSteps = ["Привет, как", "Привет, как у тебя", SourceText];

    private WebClientTester Tester => field ??= AppHost.NewWebClientTester(Out);

    [Fact(Timeout = 90_000)]
    public Task DubShouldSpeakTheStableTranslation()
        => AssertDubSpeaksTheTranslation(TimeSpan.FromMilliseconds(100), mustRequestBeforeEntry: false);

    [Fact(Timeout = 90_000)]
    public Task DubShouldWaitForTheEntryTheTranslationIsKeyedBy()
        => AssertDubSpeaksTheTranslation(TimeSpan.FromMilliseconds(300), mustRequestBeforeEntry: true);

    // Private methods

    private async Task AssertDubSpeaksTheTranslation(TimeSpan entryDelay, bool mustRequestBeforeEntry)
    {
        // arrange
        FakeTranslator.Reset();
        await Tester.SignInAsUniqueAlice();
        var (chatId, _) = await Tester.CreateChat(false);
        var services = Tester.AppServices;
        var backend = services.GetRequiredService<IAudioStreamingBackend>();
        var recorder = services.GetRequiredService<RecordingSpeechSynthesizer>();
        var sourceId = StreamId.New(services.MeshWatcher().ThisNode.Ref);
        var dubId = StreamId.New(sourceId, Languages.English);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));
        var ct = cts.Token;

        var source = Channel.CreateUnbounded<TranscriptDiff>();
        var pushSourceTask = BackgroundTask.Run(
            () => backend.PushTranscript(sourceId, new RpcStream<TranscriptDiff>(source.Reader.ReadAllAsync(ct)), ct),
            ct);
        var last = Transcript.Empty;
        Push(Unstable(SourceSteps[0]));
        await backend.WhenTranscriptPublished(sourceId, ct);

        // act
        var createEntryTask = BackgroundTask.Run(async () => {
            await Task.Delay(entryDelay, ct);
            await Tester.CreateStreamingEntry(chatId, Languages.Russian, streamId: sourceId.Value, cancellationToken: ct);
        }, ct);
        if (!mustRequestBeforeEntry)
            await createEntryTask;
        var stream = await backend.GetAudio(dubId, TimeSpan.Zero, ct);
        await createEntryTask;
        foreach (var step in SourceSteps.Skip(1))
            Push(Unstable(step));
        Push(Stable(SourceText));

        // assert
        stream.Should().NotBeNull("a Russian speaker is dubbed for an English listener");
        var chunks = await recorder.WhenSpoken(dubId.Value, 1, ct);
        chunks.Should().Equal(
            [FakeTranslator.Translated(SourceText, Languages.English)],
            "only the stable translation is spoken, and once");
        var captions = await backend.GetTranscript(dubId, ct);
        captions.Should().NotBeNull("the caption reader must get the same translated stream");

        source.Writer.Complete();
        await pushSourceTask.SilentAwait(false);
        return;

        void Push(Transcript transcript) {
            source.Writer.TryWrite(transcript - last);
            last = transcript;
        }
    }

    private static Transcript Unstable(string text)
        => new(text, LinearMap.Zero.Append(new Vector2(text.Length, text.Length)), [Languages.Russian]);

    private static Transcript Stable(string text)
        => Unstable(text) with { IsStable = true };
}

[CollectionDefinition(nameof(DubbingTranslationCollection))]
public sealed class DubbingTranslationCollection : ICollectionFixture<DubbingTranslationCollection.AppHostFixture>
{
    public sealed class AppHostFixture(IMessageSink messageSink)
        : ActualChat.Testing.Host.AppHostFixture(
            "dubbing-translation",
            messageSink,
            TestAppHostOptions.Default with {
                ConfigureHost = (_, cfg) => {
                    cfg.AddInMemory<ChatSettings>((x => x.IsTranslationEnabled, "true"));
                    cfg.AddInMemory<CoreServerSettings>((x => x.OpenAIKey, "test-key"));
                },
                ConfigureServices = (_, services) => {
                    services.AddSingleton<Translator>(c => new FakeTranslator(c));
                    services.AddKeyedSingleton<Translator>(
                        Constants.Translation.RealtimeServiceKey,
                        (c, key) => new FakeTranslator(c, (string)key));
                    services.AddSingleton<RecordingSpeechSynthesizer>();
                    services.AddSingleton<ISpeechSynthesizer>(c => c.GetRequiredService<RecordingSpeechSynthesizer>());
                },
            });
}
