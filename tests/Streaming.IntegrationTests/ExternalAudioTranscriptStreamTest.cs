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

    private static async IAsyncEnumerable<AudioFrame> WithLeadingSilence(
        IAsyncEnumerable<AudioFrame> frames,
        TimeSpan delay)
    {
        await Task.Delay(delay).ConfigureAwait(false);
        await foreach (var frame in frames.ConfigureAwait(false))
            yield return frame;
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
