using ActualChat.Audio;
using ActualChat.Live;
using ActualChat.Chat;
using ActualChat.Testing;
using ActualChat.Testing.Host;
using ActualChat.Transcription;

namespace ActualChat.Streaming.IntegrationTests;

[Collection(nameof(SpeechCollection))]
public sealed class SpeakModeTest(SpeechCollection.AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<SpeechCollection.AppHostFixture>(fixture, @out)
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

    [Fact]
    public async Task ShouldOfferATextEntryAsSomethingToJoin()
    {
        // Registering the audio makes it discoverable to someone already listening, but nothing
        // invites anyone in: without a session the chat shows no activity and no Join, so a bot
        // speaking to a room where nobody happens to be listening is heard by no one.

        // arrange
        var liveSessions = AppHost.Services.GetRequiredService<ILiveSessionsBackend>();

        // act
        var (chatId, stream) = await StreamBotText("Is there anyone here to join and hear this?");

        // assert
        await TestWait.When(async ct => {
            var hasRecorder = await liveSessions.HasRecorder(chatId, ct);
            hasRecorder.Should().BeTrue("a speaking bot is activity someone can join");
        }, WaitTimeout);

        await Tester.Chats.FinishEntryStream(Tester.Session, stream.Id, default);
    }

    [Fact]
    public async Task ShouldReachSomeoneListeningToTheChat()
    {
        // The id a listener asks for is the one it was handed - the stream's own, with no language
        // suffix. Serving speech only on a dub id leaves the bot silent to the very people the
        // registration was meant to reach, which is what a listening client actually does.

        // arrange - listening before the bot says anything, as someone already in the room is
        await Tester.SignInAsUniqueAlice();
        var (chatId, _) = await Tester.CreateChat(false);
        var live = AppHost.Services.GetRequiredService<ILiveAudioStreams>();
        var listening = await live.GetListeningStream(
            Tester.Session, chatId, Moment.EpochStart, null, CancellationToken.None);
        var heard = ReadMuxedFrames(listening, CancellationToken.None);

        // act
        var stream = await Tester.Chats.StartEntryStream(
            Tester.Session, chatId, null, Languages.German, default);
        await Tester.Chats.AppendEntryStream(
            Tester.Session, stream.Id, 0, "Kann mich hier jemand sprechen hoeren?", default);
        var entry = await ChatsBackend.GetEntry(stream.EntryId, CancellationToken.None);

        // assert - more than a frame or two, because a mix whose synthesis failed still emits one
        var frames = await heard;
        frames.Count.Should().BeGreaterThan(2, "a listener must hear the entry, not a lone frame");
        // A speech stream carries no language suffix to read a voice off, so this is the only
        // place the declared language can come from - and a real provider rejects it missing.
        await TestWait.When(ct => {
            FakeSpeechSynthesizer.SynthesizedLanguage(entry!.ContentStreamId)
                .Should().Be(Languages.German, "it must be spoken in the language it was written in");
            return Task.CompletedTask;
        }, WaitTimeout);

        await Tester.Chats.FinishEntryStream(Tester.Session, stream.Id, default);
    }

    // Private methods

    private static Task<List<MuxedAudioFrame>> ReadMuxedFrames(
        IAsyncEnumerable<MuxedAudioStreamItem> items, CancellationToken cancellationToken)
        => Task.Run(async () => {
            var frames = new List<MuxedAudioFrame>();
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(WaitTimeout);
            try {
                await foreach (var item in items.WithCancellation(cts.Token).ConfigureAwait(false)) {
                    if (item is MuxedAudioFrame frame)
                        frames.Add(frame);
                    if (frames.Count >= 10)
                        break;
                }
            }
            catch (OperationCanceledException) { }
            return frames;
        }, CancellationToken.None);

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

    [Fact]
    public async Task ShouldAnnounceATextStreamAsSomethingToListenTo()
    {
        // The registration is what makes a listener's client discover the stream and ask for it -
        // without it nothing ever requests speech, so the bot is silent to someone who is
        // listening to everyone else in the room.

        // arrange
        var (chatId, stream) = await StreamBotText("Announce me to the listeners please.");

        // act, assert
        var live = AppHost.Services.GetRequiredService<ILiveAudioStreams>();
        await TestWait.When(async ct => {
            var streams = await live.List(Tester.Session, chatId, ct);
            streams.Should().Contain(x => !x.IsTextOnly,
                "a text stream that can be spoken must be offered as audio, not marked text-only");
        }, WaitTimeout);

        await Tester.Chats.FinishEntryStream(Tester.Session, stream.Id, default);
    }
}
