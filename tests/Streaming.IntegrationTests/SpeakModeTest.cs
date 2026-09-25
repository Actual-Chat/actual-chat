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
    private const string TextRun =
        "This is a long run of words that a voice has to say out loud one after another, "
        + "which takes it a great deal longer than it took to write them down. ";

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
    public async Task ShouldTellAProducerHowFarBehindTheVoiceIs()
    {
        // No constant can tell a producer whether it is outrunning the voice reading it: speaking
        // rate depends on the language, the voice and the provider. The server knows, so it says.

        // arrange - listening first, so there is a voice to fall behind
        await Tester.SignInAsUniqueAlice();
        var (chatId, _) = await Tester.CreateChat(false);
        var live = AppHost.Services.GetRequiredService<ILiveAudioStreams>();
        var listening = await live.GetListeningStream(
            Tester.Session, chatId, Moment.EpochStart, null, CancellationToken.None);
        var heard = ReadMuxedFrames(listening, CancellationToken.None);

        // act - far more text than a voice can say in the time it takes to send it
        var stream = await Tester.Chats.StartEntryStream(
            Tester.Session, chatId, null, Languages.English, default);
        var offset = 0;
        for (var i = 0; i < 8; i++) {
            var appended = await Tester.Chats.AppendEntryStream(
                Tester.Session, stream.Id, offset, TextRun, default);
            offset = appended.Offset;
        }

        // assert - once the listener is hearing it, the voice exists to be behind
        var frames = await heard;
        frames.Should().NotBeEmpty();
        await TestWait.When(async ct => {
            var appended = await Tester.Chats.AppendEntryStream(Tester.Session, stream.Id, offset, "", ct);
            appended.SpeechBacklog.Should().NotBeNull("someone is listening, so something is speaking");
            appended.SpeechBacklog!.Value.Should().BePositive(
                "the producer wrote far more than the voice has had time to say");
        }, WaitTimeout);

        await Tester.Chats.FinishEntryStream(Tester.Session, stream.Id, default);
    }

    [Fact]
    public async Task ShouldReadAMessageTypedIntoALiveConversation()
    {
        // Someone who can't talk right now types instead. A listener would otherwise never know
        // it was said - the message is part of the conversation, so it is spoken like the rest.

        // arrange - a live session, and someone listening to it
        await Tester.SignInAsUniqueAlice();
        var (chatId, _) = await Tester.CreateChat(false);
        var liveSessions = AppHost.Services.GetRequiredService<ILiveSessionsBackend>();
        var author = await Tester.Authors.GetOwn(Tester.Session, chatId, CancellationToken.None);
        await liveSessions.OnStreamRegistered(
            chatId, author!.Id, null, true, true, CancellationToken.None);
        var live = AppHost.Services.GetRequiredService<ILiveAudioStreams>();
        var listening = await live.GetListeningStream(
            Tester.Session, chatId, Moment.EpochStart, null, CancellationToken.None);
        var heard = ReadMuxedFrames(listening, CancellationToken.None);

        // act - an ordinary typed message, not a stream
        await Tester.Commander.Call(
            new Chats_UpsertEntry {
                Session = Tester.Session,
                ChatId = chatId,
                LocalId = null,
                Text = "Give me two minutes and I will be there.",
            },
            CancellationToken.None);

        // assert
        var frames = await heard;
        frames.Should().NotBeEmpty("a typed message is read aloud to whoever is listening");
    }

    [Fact]
    public async Task ShouldSpeakATypedMessageWithoutItsMarkup()
    {
        // Markup is for the eye: a voice reading the asterisks and backticks aloud says something
        // nobody wrote. It is also read a little faster than a dub, which follows a human's pace.

        // arrange
        var chatId = await StartLiveSessionAndListen();

        // act
        await PostText(chatId, "**Two** minutes and I am `there`, see ||nothing||.");

        // assert
        var spoken = await WhenSpoken("Two minutes and I am there, see .");
        spoken.Options.Speed.Should().Be(Constants.Audio.SpokenTextSpeed, "text follows no human's pace");
    }

    [Fact]
    public async Task ShouldNotSpeakACodeOnlyTypedMessage()
    {
        // arrange
        var chatId = await StartLiveSessionAndListen();

        // act - the marker posted after it is spoken, so by then the code would have been too
        await PostText(chatId, "```sql\nselect 4b1f from nowhere;\n```");
        await PostText(chatId, "Marker 4b1f spoken.");

        // assert
        await WhenSpoken("Marker 4b1f spoken");
        FakeSpeechSynthesizer.FindSpoken("4b1f from nowhere").Should().BeNull("code is read, never heard");
    }

    [Fact]
    public async Task ShouldNotSpeakAWallOfText()
    {
        // arrange - the marker is at the end, which is the part a late start would still speak
        var chatId = await StartLiveSessionAndListen();
        var wall = string.Concat(Enumerable.Repeat(TextRun, 8)) + "End of wall 9c2e.";

        // act
        await PostText(chatId, wall);
        await PostText(chatId, "Marker 9c2e spoken.");

        // assert
        await WhenSpoken("Marker 9c2e spoken");
        FakeSpeechSynthesizer.FindSpoken("End of wall 9c2e").Should().BeNull(
            "a message that takes minutes to say is left for reading");
    }

    [Fact]
    public async Task ShouldStartNearTheLiveEdgeForSomeoneWhoJoinsLate()
    {
        // The whole message is already written before anyone listens, so every word of it is a
        // candidate to be skipped - and the floor is the only reason anything is spoken at all.

        // arrange
        await Tester.SignInAsUniqueAlice();
        var (chatId, _) = await Tester.CreateChat(false);
        var stream = await Tester.Chats.StartEntryStream(
            Tester.Session, chatId, null, Languages.English, default);
        var offset = 0;
        var written = "";
        for (var i = 1; i <= 8; i++) {
            var run = $"Run {i}. {TextRun}";
            var appended = await Tester.Chats.AppendEntryStream(
                Tester.Session, stream.Id, offset, run, default);
            offset = appended.Offset;
            written += run;
            // Spaced past the streamer's coalescing window, so these arrive as separate diffs -
            // one diff for the lot would never exercise deciding where to start.
            await Task.Delay(TimeSpan.FromSeconds(0.3));
        }
        var entry = await ChatsBackend.GetEntry(stream.EntryId, CancellationToken.None);

        // act - join only now
        var live = AppHost.Services.GetRequiredService<ILiveAudioStreams>();
        var listening = await live.GetListeningStream(
            Tester.Session, chatId, Moment.EpochStart, null, CancellationToken.None);
        var heard = ReadMuxedFrames(listening, CancellationToken.None);

        // assert
        var frames = await heard;
        frames.Should().NotBeEmpty("the floor guarantees there is something left to hear");
        await TestWait.When(ct => {
            var spoken = FakeSpeechSynthesizer.SpokenText(entry!.ContentStreamId);
            spoken.Should().NotBeEmpty();
            written.Should().Contain(spoken, "only what was written is spoken");
            written.IndexOf(spoken, StringComparison.Ordinal).Should().BeGreaterThan(0,
                "the joiner starts near the live edge, not at the first word");
            spoken.Should().NotContain("Run 1.", "what came before is read rather than heard");
            return Task.CompletedTask;
        }, WaitTimeout);

        await Tester.Chats.FinishEntryStream(Tester.Session, stream.Id, default);
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

    // A live session with someone listening to it: the listening is what makes the server speak
    private async Task<ChatId> StartLiveSessionAndListen()
    {
        await Tester.SignInAsUniqueAlice();
        var (chatId, _) = await Tester.CreateChat(false);
        var liveSessions = AppHost.Services.GetRequiredService<ILiveSessionsBackend>();
        var author = await Tester.Authors.GetOwn(Tester.Session, chatId, CancellationToken.None);
        await liveSessions.OnStreamRegistered(
            chatId, author!.Id, null, true, true, CancellationToken.None);
        var live = AppHost.Services.GetRequiredService<ILiveAudioStreams>();
        var listening = await live.GetListeningStream(
            Tester.Session, chatId, Moment.EpochStart, null, CancellationToken.None);
        _ = ReadMuxedFrames(listening, CancellationToken.None);
        return chatId;
    }

    // Polled: the fake's record is a side effect, not a computed value, so nothing invalidates it
    private static Task<(string StreamId, SpeechSynthesisOptions Options)> WhenSpoken(string fragment)
        => TestWait.WhenPolled<(string StreamId, SpeechSynthesisOptions Options)>(() => {
            var spoken = FakeSpeechSynthesizer.FindSpoken(fragment);
            spoken.Should().NotBeNull($"'{fragment}' is spoken to whoever is listening");
            return Task.FromResult(spoken!.Value);
        }, WaitTimeout);

    private Task PostText(ChatId chatId, string text)
        => Tester.Commander.Call(
            new Chats_UpsertEntry {
                Session = Tester.Session,
                ChatId = chatId,
                LocalId = null,
                Text = text,
            },
            CancellationToken.None);

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
