using ActualChat.Audio;
using ActualChat.Chat;
using ActualChat.Testing.Host;
using ActualChat.Transcription;
using ActualLab.IO;
using ActualLab.Rpc;

namespace ActualChat.Streaming.IntegrationTests;

[Collection(nameof(StreamingCollection))]
public class ExternalAudioTranscriptStreamTest(AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(30);

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
            CancellationToken.None);

        // assert
        var entry = await WhenEntryFinalized(chatsBackend, record);
        entry.Content.Should().Be("Hello world");
        entry.IsContentStreaming.Should().BeFalse();
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
            CancellationToken.None);

        // assert - no exception, and nothing is left streaming
        var entries = await ListEntries(chatsBackend, record.ChatId);
        entries.Should().OnlyContain(e => !e.IsContentStreaming);
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

    private async Task<IAsyncEnumerable<AudioFrame>> ReadFrames()
    {
        var path = new FilePath(Environment.CurrentDirectory) & "data" & "0000.opuss";
        var byteStream = path.ReadByteStream(1024, CancellationToken.None);
        var converter = new ActualOpusStreamConverter(MomentClockSet.Default, AppHost.Services.LogFor<AudioSource>());
        var audio = await converter.FromByteStream(byteStream, CancellationToken.None);
        return audio.GetFrames(CancellationToken.None);
    }

    private static async Task<ChatEntry> WhenEntryFinalized(IChatsBackend chatsBackend, AudioRecord record)
    {
        ChatEntry entry = null!;
        await TestWait.When(async ct => {
            var entries = await ListEntries(chatsBackend, record.ChatId, ct);
            var found = entries.LastOrDefault(e => !e.IsContentStreaming && !e.Content.IsNullOrEmpty());
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
