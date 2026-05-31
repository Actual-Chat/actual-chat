using ActualChat.Audio;
using ActualChat.Chat.Module;
using ActualChat.Streaming;
using ActualChat.Streaming.Module;
using ActualChat.Testing.Host;
using ActualChat.Transcription;
using ActualLab.Rpc;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ActualChat.Chat.IntegrationTests;

[Collection(nameof(RetranscribeTranslationCollection))]
public class RetranscribeTranslationFlowTest(
    RetranscribeTranslationCollection.AppHostFixture fixture,
    ITestOutputHelper @out)
    : SharedAppHostTestBase<RetranscribeTranslationCollection.AppHostFixture>(fixture, @out)
{
    private WebClientTester Tester => field ??= AppHost.NewWebClientTester(Out);
    private ITranslations Translations => Tester.Translations;

    [Fact]
    public async Task ReTranslationUsesRefinedTranscriptAfterFinalization()
    {
        // arrange
        FakeRefineTranscriber.Reset();
        await Tester.SignInAsUniqueAlice();
        var (chatId, _) = await Tester.CreateChat(false);
        var targetLanguage = Languages.Russian;

        var services = Tester.AppServices;
        var session = Tester.Session;
        var userSettingsUI = services.UserSettingsUI(session);
        await userSettingsUI.UserLanguageSettings()
            .Set(new UserLanguageSettings { Primary = Languages.English }, CancellationToken.None);
        await userSettingsUI.ChatUserSettings(chatId)
            .Update(x => x with { Language = Languages.English, VoiceMode = VoiceMode.TextAndVoice }, CancellationToken.None);

        var streaming = services.GetRequiredService<IAudioStreamingBackend>();
        var chatsBackend = services.GetRequiredService<IChatsBackend>();
        var thisNode = services.MeshWatcher().ThisNode;
        var recordStreamId = StreamId.New(thisNode.Ref);
        // Mirrors OpenAudioSegment.GetStreamId(record, 0): the transcript stream id of segment #0.
        var segmentStreamId = StreamId.New(recordStreamId.NodeRef, $"{recordStreamId.LocalId}-0000");
        var record = new AudioRecord(
            recordStreamId,
            session,
            chatId,
            services.Clocks().SystemClock.Now.EpochOffset.TotalSeconds,
            null);

        // act
        // FakeRefineTranscriber blocks finalization on a gate, so the entry stays content-streaming
        // until we release it — this deterministically reproduces the realtime-stream → finalize race.
        var recordTask = streaming.ProcessAudio(record, 0,
            new RpcStream<AudioFrame>(GenerateAudioFrames(120)),
            CancellationToken.None);

        var streamingEntry = await WhenStreamingEntry(chatsBackend, chatId, segmentStreamId);
        var entryId = streamingEntry.Id;

        // Requesting the translated transcript stream starts the realtime translation worker,
        // which enqueues the refined-transcript re-translation once the entry finalizes.
        var translatedStreamId = StreamId.New(segmentStreamId, targetLanguage);
        await streaming.GetTranscript(translatedStreamId, CancellationToken.None);

        FakeRefineTranscriber.Release();
        await recordTask;

        var finalEntry = await chatsBackend.GetEntry(entryId, CancellationToken.None);
        finalEntry.Should().NotBeNull();
        finalEntry!.IsContentStreaming.Should().BeFalse();
        finalEntry.Content.Should().Contain(FakeRefineTranscriber.Marker);

        // assert
        // The stored translation must upgrade to the refined transcript's translation on its own
        // (translateIfMissing: false — no reader pulls it). Before the fix the re-translation was
        // dropped while the entry was still content-streaming, leaving the realtime translation.
        await ComputedTest.When(async ct => {
            var translation = await Translations.Get(session, TranslationId.New(entryId, targetLanguage), false, ct);
            translation.Should().NotBeNull();
            translation!.IsStreaming.Should().BeFalse();
            translation.Content.Should().Contain(FakeRefineTranscriber.Marker);
            translation.SourceContentHash.Should().Be(finalEntry.ContentHash);
        }, TimeSpan.FromSeconds(20));
    }

    // Private methods

    private static Task<ChatEntry> WhenStreamingEntry(IChatsBackend chatsBackend, ChatId chatId, StreamId segmentStreamId)
        => ComputedTest.When(async ct => {
            var range = await chatsBackend.GetLidRange(chatId, true, ct);
            var idTile = Constants.Chat.ServerIdTileStack.FirstLayer.GetTile(Math.Max(range.End - 1, 0));
            var tile = await chatsBackend.GetTile(chatId, idTile.Range, false, ct);
            var entry = tile.Entries.LastOrDefault(e => e.ContentStreamId == segmentStreamId.Value && e.IsContentStreaming);
            entry.Should().NotBeNull();
            return entry!;
        }, TimeSpan.FromSeconds(20));

    private static async IAsyncEnumerable<AudioFrame> GenerateAudioFrames(int frameCount)
    {
        var offset = TimeSpan.Zero;
        var frameDuration = TimeSpan.FromMilliseconds(20);
        for (var i = 0; i < frameCount; i++) {
            var data = new byte[100];
            Array.Fill(data, (byte)(i % 256));
            yield return new AudioFrame {
                Data = data,
                Offset = offset,
                Duration = frameDuration,
            };
            offset += frameDuration;
            await Task.Delay(1).ConfigureAwait(false);
        }
    }
}

[CollectionDefinition(nameof(RetranscribeTranslationCollection))]
public sealed class RetranscribeTranslationCollection : ICollectionFixture<RetranscribeTranslationCollection.AppHostFixture>
{
    public sealed class AppHostFixture(IMessageSink messageSink)
        : ActualChat.Testing.Host.AppHostFixture(
            "retranscribe-translation",
            messageSink,
            TestAppHostOptions.Default with {
                ConfigureHost = (_, cfg) => {
                    cfg.AddInMemory<ChatSettings>(
                        (x => x.IsTranslationEnabled, "true"),
                        (x => x.IsRetranscriptionEnabled, "true"),
                        (x => x.OpenAIKey, "test-key"));
                    cfg.AddInMemory<StreamingSettings>((x => x.UseFakeTranscriber, "true"));
                },
                ConfigureServices = (_, services) => {
                    services.AddSingleton<Translator>(c => new FakeTranslator(c));
                    services.AddKeyedSingleton<Translator>(
                        Constants.Translation.RealtimeServiceKey,
                        (c, key) => new FakeTranslator(c, (string)key));
                    services.Replace(ServiceDescriptor.Singleton<IRefineTranscriber>(_ => new FakeRefineTranscriber()));
                },
            });
}

// Deterministic translator: prefixes the source so the stored translation reveals which
// transcript (realtime vs refined) it was derived from.
sealed file class FakeTranslator(IServiceProvider services, string serviceKey = Constants.Translation.ServiceKey)
    : Translator(services, serviceKey)
{
    public const string Prefix = "T:";

    public override Task<string> Translate(
        string textToTranslate,
        Language targetLanguage,
        TranslationResult[] context,
        CancellationToken cancellationToken)
        => Task.FromResult(Prefix + textToTranslate);

    public override async IAsyncEnumerable<StringDiff> Stream(
        string textToTranslate,
        Language targetLanguage,
        TranslationResult[] context,
        [EnumeratorCancellation] CancellationToken cancellationToken)
    {
        await Task.CompletedTask.ConfigureAwait(false);
        yield return StringDiff.New(Prefix + textToTranslate, "");
    }
}

sealed file class FakeRefineTranscriber : IRefineTranscriber
{
    public const string Marker = "RefineTranscriptionTestMarker";
    public const string FinalText =
        Marker + " — final retranscribed text from the recording, made deliberately long so it deterministically "
        + "beats the realtime FakeTranscriber output and is chosen as the finalized entry content regardless of template.";

    private static TaskCompletionSource _gate = TaskCompletionSourceExt.New();

    public static void Reset()
        => _gate = TaskCompletionSourceExt.New();

    public static void Release()
        => _gate.TrySetResult();

    public Task<Transcript?> Transcribe(
        AudioSource audioSource,
        TranscriptionOptions options,
        CancellationToken cancellationToken = default)
        => TranscribeImpl(options, cancellationToken);

    private static async Task<Transcript?> TranscribeImpl(TranscriptionOptions options, CancellationToken cancellationToken)
    {
        await _gate.Task.WaitAsync(cancellationToken).ConfigureAwait(false);
        return new Transcript(FinalText, LinearMap.Zero, [options.Language]);
    }
}
