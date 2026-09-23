using ActualChat.Audio;
using ActualChat.Chat;
using ActualChat.Testing.Host;
using ActualChat.Transcription;

namespace ActualChat.Streaming.IntegrationTests;

[Collection(nameof(StreamingCollection))]
public class SpeakModeTest(AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(30);

    private WebClientTester Tester => field ??= AppHost.NewWebClientTester(Out);
    private IAudioStreamingBackend StreamingBackend
        => field ??= AppHost.Services.GetRequiredService<IAudioStreamingBackend>();
    private IChatsBackend ChatsBackend => field ??= AppHost.Services.GetRequiredService<IChatsBackend>();

    protected override async Task DisposeAsync()
    {
        await Tester.DisposeSilentlyAsync();
        await base.DisposeAsync();
    }

    [Fact]
    public async Task ShouldSpeakATextOnlyEntry()
    {
        // arrange - a bot streams text, no audio
        var (chatId, stream) = await StreamBotText("Hello there, this is a bot speaking.");

        // act - ask to hear it while it is still streaming
        var entry = await ChatsBackend.GetEntry(stream.EntryId, default);
        entry!.ContentStreamId.Should().NotBeEmpty();
        var speechStreamId = StreamId.New(StreamId.Parse(entry.ContentStreamId), Languages.English);
        var audio = await StreamingBackend.GetAudio(speechStreamId, TimeSpan.Zero, CancellationToken.None);

        // assert
        audio.Should().NotBeNull("a text-only entry must be speakable");
        var frames = await ReadSomeFrames(audio!);
        frames.Should().NotBeEmpty("the synthesizer should produce audio for the bot's text");

        await Tester.Chats.FinishEntryStream(Tester.Session, stream.Id, default);
    }

    [Fact]
    public async Task ShouldNotSpeakAnEntryThatAlreadyHasAudio()
    {
        // A human voice message must never be synthesized over: it is a registered recording, so
        // the speech producer must not claim it.

        // arrange
        await Tester.SignInAsUniqueAlice();
        var (chatId, _) = await Tester.CreateChat(false);
        var streamId = StreamId.New(AppHost.Services.MeshWatcher().ThisNode.Ref);

        // act - a stream the producer never declared text-only, i.e. anything with audio
        var dubStreamId = StreamId.New(streamId, Languages.Russian);
        await StreamingBackend.GetAudio(dubStreamId, TimeSpan.Zero, CancellationToken.None);
        await Task.Delay(TimeSpan.FromSeconds(2));

        // assert - routed to the dub path, so this stream was never spoken for
        FakeSpeechSynthesizer.WasSynthesized(dubStreamId.Value).Should().BeFalse(
            "only a producer that declared its stream text-only is spoken for");
    }

    // Private methods

    private async Task<(ChatId, ChatEntryStream)> StreamBotText(string text)
    {
        await Tester.SignInAsUniqueAlice();
        var (chatId, _) = await Tester.CreateChat(false);
        var stream = await Tester.Chats.StartEntryStream(Tester.Session, chatId, null, null, default);
        await Tester.Chats.AppendEntryStream(Tester.Session, stream.Id, 0, text, default);
        return (chatId, stream);
    }

    private static async Task<List<AudioFrame>> ReadSomeFrames(IAsyncEnumerable<AudioFrame> audio)
    {
        var frames = new List<AudioFrame>();
        using var cts = new CancellationTokenSource(WaitTimeout);
        await foreach (var frame in audio.WithCancellation(cts.Token).ConfigureAwait(false)) {
            frames.Add(frame);
            if (frames.Count >= 5)
                break;
        }
        return frames;
    }
}
