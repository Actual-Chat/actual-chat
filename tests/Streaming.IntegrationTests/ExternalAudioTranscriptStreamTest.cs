using ActualChat.Audio;
using ActualChat.Chat;
using ActualChat.Live;
using ActualChat.Testing.Host;
using ActualChat.Transcription;
using ActualLab.IO;
using ActualLab.Rpc;

namespace ActualChat.Streaming.IntegrationTests;

[Collection(nameof(StreamingCollection))]
public sealed class ExternalAudioTranscriptStreamTest(AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(30);
    private static readonly TimeSpan MinAudioDuration = TimeSpan.FromSeconds(1);
    // Comfortably past Constants.Audio.FrameSilenceTimeout, which governs a microphone
    private static readonly TimeSpan FirstFrameDelay = TimeSpan.FromSeconds(4);

    private WebClientTester Tester => field ??= AppHost.NewWebClientTester(Out);

    protected override async Task DisposeAsync()
    {
        await Tester.DisposeSilentlyAsync();
        await base.DisposeAsync();
    }

    [Fact]
    public async Task ShouldFinalizeAPlayableEntryFromExternalAudioAndText()
    {
        // arrange
        var (backend, chatsBackend, record) = await NewRecording();

        // act
        await backend.ProcessAudioWithTranscript(
            record,
            0,
            new RpcStream<AudioFrame>(await ReadFrames()),
            new RpcStream<ExternalTranscriptChunk>(new[] {
                new ExternalTranscriptChunk("Hello ", true, 0.5, true),
                new ExternalTranscriptChunk("world", true, 1.0, true),
            }.ToAsyncEnumerable()),
            null,
            CancellationToken.None);

        // assert
        var entry = await WhenEntryFinalized(chatsBackend, record);
        entry.Content.Should().Be("Hello world");
        entry.IsContentStreaming.Should().BeFalse();
        entry.HasAudio.Should().BeTrue("the entry must be playable, not just readable");
    }

    [Fact]
    public async Task ShouldFinalizeAPlayableEntryWhenNoTextArrives()
    {
        // A bot that speaks but sends no transcript still produced audio someone can play.

        // arrange
        var (backend, chatsBackend, record) = await NewRecording();

        // act
        await backend.ProcessAudioWithTranscript(
            record,
            0,
            new RpcStream<AudioFrame>(await ReadFrames()),
            new RpcStream<ExternalTranscriptChunk>(AsyncEnumerable.Empty<ExternalTranscriptChunk>()),
            null,
            CancellationToken.None);

        // assert - the audio is the message even with no words. Only a real voice entry has
        // Audio, so a system "joined the chat" notice cannot satisfy this.
        // Only a real voice entry has Audio, so a system "joined the chat" notice cannot satisfy
        // this - and it is polled, because finalization attaches the audio after the entry exists.
        await TestWait.When(async ct => {
            var entries = await ListEntries(chatsBackend, record.ChatId, ct);
            entries.Should().Contain(e => e.HasAudio,
                "a producer that sent audio meant to post something playable");
        }, WaitTimeout);
    }

    [Fact]
    public async Task ShouldPostAVoiceMessageFromOneHeldStream()
    {
        // The stream-capable path: a caller that can hold a stream open shouldn't have to carry
        // offsets and chunk base64 the way an MCP client must.

        // arrange
        await Tester.SignInAsUniqueAlice();
        var (chatId, _) = await Tester.CreateChat(false);
        var chatsBackend = AppHost.Services.GetRequiredService<IChatsBackend>();
        var ogg = await File.ReadAllBytesAsync(
            (new FilePath(Environment.CurrentDirectory) & "data" & "soniox-tts-sample.opus").Value);

        // act
        var entry = await Tester.Chats.StreamVoice(
            Tester.Session,
            chatId,
            null,
            Languages.English,
            new RpcStream<VoiceStreamPart>(Parts(ogg)),
            CancellationToken.None);

        // assert
        entry.Should().NotBeNull("a held stream posts the message it carried");
        entry!.Content.Should().Be("Spoken in one go");
        entry.HasAudio.Should().BeTrue();
        entry.Duration.Should().BeGreaterThan(MinAudioDuration.TotalSeconds,
            "the whole clip must arrive, not just its first chunk");
        _ = await chatsBackend.GetEntry(entry.Id, CancellationToken.None);
    }

    [Fact]
    public async Task ShouldStopReportingARecorderOnceTheStreamEnds()
    {
        // A recording client clears its own participation when it stops. A producer has no client,
        // so nothing said "done" and the chat kept reporting someone talking - with no sound behind
        // it - until the 90-second staleness cutoff let go.

        // arrange
        var (backend, _, record) = await NewRecording();
        var liveSessions = AppHost.Services.GetRequiredService<ILiveSessionsBackend>();

        // act
        await backend.ProcessAudioWithTranscript(
            record,
            0,
            new RpcStream<AudioFrame>(await ReadFrames()),
            new RpcStream<ExternalTranscriptChunk>(new[] {
                new ExternalTranscriptChunk("Done speaking", true, 0.2, true),
            }.ToAsyncEnumerable()),
            null,
            CancellationToken.None);

        // assert
        await TestWait.When(async ct => {
            var hasRecorder = await liveSessions.HasRecorder(record.ChatId, ct);
            hasRecorder.Should().BeFalse("the sound is over, so nothing is recording");
        }, WaitTimeout);
    }

    [Fact]
    public async Task ShouldLowerARaisedHandOnceItsOwnerSaysSeveralWords()
    {
        // arrange
        var (backend, _, record) = await NewRecording();
        var (liveSessions, authorId) = await RaiseHand(record);
        var whenChecked = TaskCompletionSourceExt.New();

        // act - the audio stays open, since a hand also goes away with the stream that held its owner in
        var processTask = backend.ProcessAudioWithTranscript(
            record,
            0,
            new RpcStream<AudioFrame>(WithTrailingPause(await ReadFrames(), whenChecked.Task)),
            new RpcStream<ExternalTranscriptChunk>(new[] {
                new ExternalTranscriptChunk("Thanks, so what I wanted to say", true, 0.5, true),
            }.ToAsyncEnumerable()),
            null,
            CancellationToken.None);

        // assert
        try {
            await TestWait.When(async ct => {
                var live = await liveSessions.Get(record.ChatId, ct);
                live!.Members.Single(m => m.AuthorId == authorId).IsHandRaised.Should()
                    .BeFalse("whoever speaks has the floor already");
            }, WaitTimeout);
        }
        finally {
            whenChecked.TrySetResult();
            await processTask;
        }
    }

    [Fact]
    public async Task ShouldKeepARaisedHandWhenItsOwnerSaysJustAFewWords()
    {
        // arrange
        var (backend, chatsBackend, record) = await NewRecording();
        var (liveSessions, authorId) = await RaiseHand(record);
        var whenChecked = TaskCompletionSourceExt.New();

        // act
        var processTask = backend.ProcessAudioWithTranscript(
            record,
            0,
            new RpcStream<AudioFrame>(WithTrailingPause(await ReadFrames(), whenChecked.Task)),
            new RpcStream<ExternalTranscriptChunk>(new[] {
                new ExternalTranscriptChunk("Yes, I agree", true, 0.5, true),
            }.ToAsyncEnumerable()),
            null,
            CancellationToken.None);

        // assert - the entry is created from the words the hand is judged by, so they were seen by now
        try {
            await TestWait.When(async ct => {
                var entries = await ListEntries(chatsBackend, record.ChatId, ct);
                entries.Should().Contain(e => e.IsContentStreaming);
            }, WaitTimeout);
            var live = await liveSessions.Get(record.ChatId, CancellationToken.None);
            live!.Members.Single(m => m.AuthorId == authorId).IsHandRaised.Should()
                .BeTrue("a short remark isn't taking the floor");
        }
        finally {
            whenChecked.TrySetResult();
            await processTask;
        }
    }

    [Fact]
    public async Task ShouldWaitWhileAProducerFillsItsNextOggPage()
    {
        // A producer hands over whole Ogg pages, so nothing decodes until a page completes - and
        // an LLM may think for seconds between them. The microphone-grade silence watchdog would
        // end the stream mid-word and save a message with no sound in it.

        // arrange
        var (backend, chatsBackend, record) = await NewRecording();
        var frames = WithLeadingSilence(await ReadFrames(), FirstFrameDelay);

        // act
        await backend.ProcessAudioWithTranscript(
            record,
            0,
            new RpcStream<AudioFrame>(frames),
            new RpcStream<ExternalTranscriptChunk>(new[] {
                new ExternalTranscriptChunk("Slow but spoken", true, null, true),
            }.ToAsyncEnumerable()),
            null,
            CancellationToken.None);

        // assert
        var entry = await WhenEntryFinalized(chatsBackend, record);
        entry.Content.Should().Be("Slow but spoken");
        entry.Audio!.Duration.Should().BeGreaterThan(1, "the audio must survive the producer's pause");
    }

    [Fact]
    public async Task ShouldFinalizeWhenTheTranscriptEndsBeforeTheAudio()
    {
        // arrange
        var (backend, chatsBackend, record) = await NewRecording();

        // act - one chunk, then the transcript stream closes while frames keep coming
        await backend.ProcessAudioWithTranscript(
            record,
            0,
            new RpcStream<AudioFrame>(await ReadFrames()),
            new RpcStream<ExternalTranscriptChunk>(
                new[] { new ExternalTranscriptChunk("Only this", true, 0.2, true) }.ToAsyncEnumerable()),
            null,
            CancellationToken.None);

        // assert
        var entry = await WhenEntryFinalized(chatsBackend, record);
        entry.Content.Should().Be("Only this");
    }

    // Private methods

    private async Task<(IAudioStreamingBackend, IChatsBackend, AudioRecord)> NewRecording()
    {
        var services = AppHost.Services;
        await Tester.SignInAsUniqueAlice();
        var (chatId, _) = await Tester.CreateChat(false);
        var backend = services.GetRequiredService<IAudioStreamingBackend>();
        var chatsBackend = services.GetRequiredService<IChatsBackend>();
        var streamId = StreamId.New(services.MeshWatcher().ThisNode.Ref);
        var record = new AudioRecord(
            streamId, Tester.Session, chatId,
            CpuClock.Instance.Now.EpochOffset.TotalSeconds, null);
        return (backend, chatsBackend, record);
    }

    private async Task<(ILiveSessionsBackend, AuthorId)> RaiseHand(AudioRecord record)
    {
        var chatId = record.ChatId;
        var liveSessions = AppHost.Services.GetRequiredService<ILiveSessionsBackend>();
        var author = await Tester.Authors.GetOwn(Tester.Session, chatId, CancellationToken.None);
        // A hand needs a session, and a session needs a second voice
        await liveSessions.OnStreamRegistered(chatId, author!.Id, null, true, true, CancellationToken.None);
        await liveSessions.OnStreamRegistered(
            chatId, AuthorId.New(chatId, 777_051), null, true, true, CancellationToken.None);
        await liveSessions.SetHandRaised(chatId, author.Id, true, CancellationToken.None);
        var live = await liveSessions.Get(chatId, CancellationToken.None);
        live!.Members.Single(m => m.AuthorId == author.Id).IsHandRaised.Should().BeTrue();
        return (liveSessions, author.Id);
    }

    private static async IAsyncEnumerable<VoiceStreamPart> Parts(byte[] ogg)
    {
        const int chunkSize = 4 * 1024;
        for (var offset = 0; offset < ogg.Length; offset += chunkSize) {
            var chunk = ogg[offset..Math.Min(offset + chunkSize, ogg.Length)];
            yield return new VoiceStreamPart(chunk, offset == 0 ? "Spoken in one go" : null, null);
            await Task.Yield();
        }
    }

    private static async IAsyncEnumerable<AudioFrame> WithLeadingSilence(
        IAsyncEnumerable<AudioFrame> frames,
        TimeSpan delay)
    {
        await Task.Delay(delay).ConfigureAwait(false);
        await foreach (var frame in frames.ConfigureAwait(false))
            yield return frame;
    }

    private static async IAsyncEnumerable<AudioFrame> WithTrailingPause(
        IAsyncEnumerable<AudioFrame> frames,
        Task whenResumed)
    {
        await foreach (var frame in frames.ConfigureAwait(false))
            yield return frame;
        await whenResumed.ConfigureAwait(false);
    }

    private async Task<IAsyncEnumerable<AudioFrame>> ReadFrames()
    {
        var path = new FilePath(Environment.CurrentDirectory) & "data" & "0000.opuss";
        var byteStream = path.ReadByteStream(1024, CancellationToken.None);
        var converter = new ActualOpusStreamConverter(MomentClockSet.Default, AppHost.Services.LogFor<AudioSource>());
        var audio = await converter.FromByteStream(byteStream, CancellationToken.None);
        return audio.GetFrames(CancellationToken.None);
    }

    private static async Task<ChatEntry> WhenEntryFinalized(
        IChatsBackend chatsBackend, AudioRecord record, bool mustHaveContent = true)
    {
        ChatEntry entry = null!;
        await TestWait.When(async ct => {
            var entries = await ListEntries(chatsBackend, record.ChatId, ct);
            var found = entries.LastOrDefault(e =>
                !e.IsContentStreaming && (!mustHaveContent || !e.Content.IsNullOrEmpty()));
            found.Should().NotBeNull();
            entry = found!;
        }, WaitTimeout);
        return entry;
    }

    private static async Task<List<ChatEntry>> ListEntries(
        IChatsBackend chatsBackend, ChatId chatId, CancellationToken cancellationToken = default)
    {
        var maxLid = await chatsBackend.GetMaxLid(chatId, false, cancellationToken);
        var tile = Constants.Chat.EntryIdTiles.GetTile(maxLid);
        var chatTile = await chatsBackend.GetTile(chatId, tile.Range, false, cancellationToken);
        return chatTile.Entries.ToList();
    }
}
