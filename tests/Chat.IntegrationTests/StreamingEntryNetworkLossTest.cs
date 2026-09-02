using ActualChat.Transcription.Module;
using ActualChat.Audio;
using ActualChat.Streaming;
using ActualChat.Testing.Host;
using ActualLab.Rpc;

namespace ActualChat.Chat.IntegrationTests;

[Collection(nameof(StreamingEntryNetworkLossCollection))]
public class StreamingEntryNetworkLossTest(
    StreamingEntryNetworkLossCollection.AppHostFixture fixture,
    ITestOutputHelper @out)
    : SharedAppHostTestBase<StreamingEntryNetworkLossCollection.AppHostFixture>(fixture, @out)
{
    private WebClientTester Tester => field ??= AppHost.NewWebClientTester(Out);

    [Fact]
    public async Task WatchdogEndSavesPartialAudioAndFinalizes()
    {
        // arrange
        await Tester.SignInAsUniqueAlice();
        var (chatId, _) = await Tester.CreateChat(false);

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
        // Simulate the client losing the network mid-recording: frames stop arriving but the stream is
        // never closed. The frame-silence watchdog must end the stream gracefully, so the audio that did
        // reach the server is saved and the entry finalizes like a short recording — not discarded, and
        // not left content-streaming forever (the pre-fix behaviour on the OCE path).
        var recordTask = streaming.ProcessAudio(record, 0,
            new RpcStream<AudioFrame>(FramesThenHang(80)),
            CancellationToken.None);

        var streamingEntry = await WhenStreamingEntry(chatsBackend, chatId, segmentStreamId);
        var entryId = streamingEntry.Id;

        // assert
        await ComputedTest.When(async ct => {
            var entry = await chatsBackend.GetEntry(entryId, ct);
            entry.Should().NotBeNull();
            entry!.IsRemoved.Should().BeFalse();
            entry.IsContentStreaming.Should().BeFalse(
                because: "the frame-silence watchdog must finalize a streaming entry on network drop");
            entry.EndsAt.Should().NotBeNull();
            entry.Audio?.MediaId.Should().NotBeNull(
                because: "the audio that reached the server before the drop must still be saved");
        }, TimeSpan.FromSeconds(30));

        // A pure silence timeout is not a cancellation: ProcessAudio completes normally.
        await recordTask;
    }

    // Private methods

    private static Task<ChatEntry> WhenStreamingEntry(IChatsBackend chatsBackend, ChatId chatId, StreamId segmentStreamId)
        => ComputedTest.When(async ct => {
            var range = await chatsBackend.GetLidRange(chatId, true, ct);
            var idTile = Constants.Chat.EntryIdTiles.GetTile(Math.Max(range.End - 1, 0));
            var tile = await chatsBackend.GetTile(chatId, idTile.Range, false, ct);
            var entry = tile.Entries.LastOrDefault(e => e.ContentStreamId == segmentStreamId.Value && e.IsContentStreaming);
            entry.Should().NotBeNull();
            return entry!;
        }, TimeSpan.FromSeconds(20));

    // Emits frameCount ~20ms frames with small real-time gaps so a transcript entry is created,
    // then blocks until the enumerator is cancelled — i.e. never closes the stream. This is the
    // client-lost-the-network case; the server's frame-silence watchdog cancels the read.
    private static async IAsyncEnumerable<AudioFrame> FramesThenHang(
        int frameCount,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
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
            await Task.Delay(5, cancellationToken).ConfigureAwait(false);
        }
        // -1ms == infinite for Task.Delay: block until the enumerator's token is cancelled.
        await Task.Delay(TimeSpan.FromMilliseconds(-1), cancellationToken).ConfigureAwait(false);
    }
}

[CollectionDefinition(nameof(StreamingEntryNetworkLossCollection))]
public sealed class StreamingEntryNetworkLossCollection
    : ICollectionFixture<StreamingEntryNetworkLossCollection.AppHostFixture>
{
    public sealed class AppHostFixture(IMessageSink messageSink)
        : ActualChat.Testing.Host.AppHostFixture(
            "streaming-entry-network-loss",
            messageSink,
            TestAppHostOptions.Default with {
                ConfigureHost = (_, cfg) => {
                    cfg.AddInMemory<TranscriptionSettings>((x => x.UseFakeTranscriber, "true"));
                },
            });
}
