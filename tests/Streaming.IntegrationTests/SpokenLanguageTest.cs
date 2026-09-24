using ActualChat.Audio;
using ActualChat.Chat;
using ActualChat.Testing;
using ActualChat.Testing.Host;
using ActualChat.Transcription;
using ActualLab.IO;
using ActualLab.Rpc;

namespace ActualChat.Streaming.IntegrationTests;

[Collection(nameof(SpokenLanguageCollection))]
public sealed class SpokenLanguageTest(SpokenLanguageCollection.AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<SpokenLanguageCollection.AppHostFixture>(fixture, @out)
{
    private static readonly TimeSpan WaitTimeout = TimeSpan.FromSeconds(30);

    private WebClientTester Tester => field ??= AppHost.NewWebClientTester(Out);

    protected override async Task DisposeAsync()
    {
        await Tester.DisposeSilentlyAsync();
        await base.DisposeAsync();
    }

    [Fact]
    public async Task ShouldRecordTheLanguageTheProducerDeclared()
    {
        // Nothing reads these words, so an undeclared entry names no language - and a listener is
        // only ever offered a translation of a message whose language is known.

        // arrange
        var services = AppHost.Services;
        await Tester.SignInAsUniqueAlice();
        var (chatId, _) = await Tester.CreateChat(false);
        var backend = services.GetRequiredService<IAudioStreamingBackend>();
        var chatsBackend = services.GetRequiredService<IChatsBackend>();
        var languagesBackend = services.GetRequiredService<IChatEntryLanguagesBackend>();
        var record = new AudioRecord(
            StreamId.New(services.MeshWatcher().ThisNode.Ref),
            Tester.Session,
            chatId,
            CpuClock.Instance.Now.EpochOffset.TotalSeconds,
            null);

        // act
        await backend.ProcessAudioWithTranscript(
            record,
            0,
            new RpcStream<AudioFrame>(await ReadFrames()),
            new RpcStream<ExternalTranscriptChunk>(new[] {
                new ExternalTranscriptChunk("Guten Tag", true, 0.5, true),
            }.ToAsyncEnumerable()),
            Languages.German,
            CancellationToken.None);

        // assert
        await TestWait.When(async ct => {
            var maxLid = await chatsBackend.GetMaxLid(chatId, false, ct);
            var tile = Constants.Chat.EntryIdTiles.GetTile(maxLid);
            var languages = await languagesBackend.GetTile(chatId, tile.Range, ct);
            languages.Entries.Should().Contain(
                x => x.Languages.Length == 1 && x.Languages[0] == Languages.German,
                "the producer said what it speaks, so the entry must say so too");
        }, WaitTimeout);
    }

    // Private methods

    private async Task<IAsyncEnumerable<AudioFrame>> ReadFrames()
    {
        var path = new FilePath(Environment.CurrentDirectory) & "data" & "0000.opuss";
        var byteStream = path.ReadByteStream(1024, CancellationToken.None);
        var converter = new ActualOpusStreamConverter(MomentClockSet.Default, AppHost.Services.LogFor<AudioSource>());
        var audio = await converter.FromByteStream(byteStream, CancellationToken.None);
        return audio.GetFrames(CancellationToken.None);
    }
}
