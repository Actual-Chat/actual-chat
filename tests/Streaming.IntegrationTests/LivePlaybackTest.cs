using ActualChat.Audio;
using ActualChat.Live;
using ActualChat.Testing.Host;
using ActualChat.UI.Blazor.App.Services;
using ActualLab.Rpc;

namespace ActualChat.Streaming.IntegrationTests;

[Collection(nameof(StreamingCollection))]
public class LivePlaybackTest(AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    [LocalFact("Requires DeepGram / Google Cloud credentials", Timeout = 30_000)]
    public async Task LiveStreamMuxer_ShouldEmitStreamItemsWhenRecording()
    {
        var appHost = AppHost;
        var services = appHost.Services;
        var commander = services.Commander();
        var session = Session.New();

        _ = await appHost.SignIn(session, new AccountFull("Bobby"));
        var log = services.LogFor<LivePlaybackTest>();
        var userSettingsUI = services.UserSettingsUI(session);

        await userSettingsUI.UserLanguageSettings().Set(
            new UserLanguageSettings { Primary = Languages.Main });

        // Create a chat
        var chat = await commander.Call(new Chats_Change(session, default, null, new() {
            Create = new ChatDiff {
                Title = "LivePlaybackTest",
                Kind = ChatKind.Group,
            },
        }));
        chat.Require();

        // Start Live Streams listener
        var liveStreams = services.GetRequiredService<ILiveAudioStreams>();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        var liveStreamTask = liveStreams.GetListeningStream(session, chat.Id, cts.Token);
        var liveStream = await liveStreamTask;

        // Collect items in background
        var receivedItems = new List<MuxedAudioStreamItem>();
        var collectTask = BackgroundTask.Run(async () => {
            log.LogInformation("Starting to collect Live items");
            try {
                await foreach (var item in liveStream.WithCancellation(cts.Token)) {
                    log.LogInformation("Received Live item: {ItemType}, StreamIndex={StreamIndex}",
                        item.GetType().Name, item.StreamIndex);
                    receivedItems.Add(item);
                }
            }
            catch (OperationCanceledException) {
                log.LogInformation("Live stream collection cancelled, received {Count} items", receivedItems.Count);
            }
        }, cts.Token);

        // Wait a bit for the muxer to start observing
        await Task.Delay(500, cts.Token);

        // Now start recording audio
        var backend = services.GetRequiredService<IAudioStreamingBackend>();
        var thisNode = services.MeshWatcher().ThisNode;
        var streamId = StreamId.New(thisNode.Ref);
        var audioRecord = new AudioRecord(
            streamId, session, chat.Id,
            CpuClock.Instance.Now.EpochOffset.TotalSeconds, null);

        log.LogInformation("Processing audio file...");
        var audioFrames = GetTestAudioFrames();
        await backend.ProcessAudio(audioRecord, 333,
            new RpcStream<AudioFrame>(audioFrames),
            cts.Token);
        log.LogInformation("Audio file processed");

        // Wait for items to be received
        await Task.Delay(2000, cts.Token);

        // Cancel collection
        await cts.CancelAsync();
        await collectTask.SilentAwait(false);

        // Verify we received items
        log.LogInformation("Total items received: {Count}", receivedItems.Count);

        // Log item details
        var startItems = receivedItems.OfType<MuxedAudioStreamStart>().ToList();
        var audioFrameItems = receivedItems.OfType<MuxedAudioFrame>().ToList();
        var endItems = receivedItems.OfType<MuxedAudioStreamEnd>().ToList();

        log.LogInformation("StreamStart items: {Count}", startItems.Count);
        log.LogInformation("AudioFrame items: {Count}", audioFrameItems.Count);
        log.LogInformation("StreamEnd items: {Count}", endItems.Count);

        // We should have at least received stream start and some frames
        receivedItems.Should().NotBeEmpty("should receive Live items when audio is being recorded");
        startItems.Should().NotBeEmpty("should receive at least one StreamStart");
    }

    [LocalFact("Requires DeepGram / Google Cloud credentials", Timeout = 30_000)]
    public async Task LiveStreamDemuxer_ShouldRaiseEventsForReceivedStreams()
    {
        var appHost = AppHost;
        var services = appHost.Services;
        var log = services.LogFor<LivePlaybackTest>();

        // Create test items
        var testChatId = ChatId.Parse("testChat");
        var testItems = new List<MuxedAudioStreamItem> {
            new MuxedAudioStreamStart {
                StreamIndex = 1,
                StreamInfo = new LiveAudioStreamInfo {
                    ChatId = testChatId,
                    AuthorId = AuthorId.New(testChatId, 1),
                    StreamId = "test-stream-1",
                    BeginsAt = SystemClock.Instance.Now,
                    Format = AudioSource.DefaultFormat,
                },
            },
            new MuxedAudioFrame { StreamIndex = 1, Data = new byte[]{1, 2, 3, 4}, Offset = TimeSpan.Zero },
            new MuxedAudioFrame {
                StreamIndex = 1,
                Data = new byte[]{5, 6, 7, 8},
                Offset = Constants.Audio.OpusFrameDuration,
            },
            new MuxedAudioStreamEnd { StreamIndex = 1 },
        };

        var streamStartedEvents = new List<int>();
        var receivedFrames = new List<ReadOnlyMemory<byte>>();
        var frameCollectionTcs = TaskCompletionSourceExt.New();

        // Create RpcStream from test items
        var rpcStream = RpcStream.New(testItems.ToAsyncEnumerable());

        // Create demuxer
        var demuxer = new AudioStreamDemuxer(rpcStream, log);
        demuxer.StreamStarted += (streamInfo, playsAt, audioFrames) => {
            log.LogInformation("StreamStarted event: #{StreamId}, PlaysAt={PlaysAt}", streamInfo.StreamId, playsAt);
            streamStartedEvents.Add(1); // Using 1 as placeholder since we no longer have StreamIndex

            // Collect audio frames synchronously in the event handler
            // to ensure we get all frames before the stream ends
            _ = CollectFrames(audioFrames, receivedFrames, frameCollectionTcs, log);
        };

        // Run demuxer
        await demuxer.Run();

        // Wait for frame collection with timeout
        try {
            await frameCollectionTcs.Task.WaitAsync(TimeSpan.FromSeconds(5));
        }
        catch (TimeoutException) {
            log.LogWarning("Frame collection timed out");
        }

        // Verify events
        log.LogInformation("Verifying: streamStartedEvents={Count}, receivedFrames={FrameCount}",
            streamStartedEvents.Count, receivedFrames.Count);
        streamStartedEvents.Should().ContainSingle().Which.Should().Be(1);
        receivedFrames.Should().HaveCount(2);
        receivedFrames[0].ToArray().Should().BeEquivalentTo(new byte[] { 1, 2, 3, 4 });
        receivedFrames[1].ToArray().Should().BeEquivalentTo(new byte[] { 5, 6, 7, 8 });
    }

    private static async Task CollectFrames(
        IAsyncEnumerable<AudioFrame> audioFrames,
        List<ReadOnlyMemory<byte>> receivedFrames,
        TaskCompletionSource frameCollectionTcs,
        ILogger log)
    {
        try {
            await foreach (var frame in audioFrames.ConfigureAwait(false)) {
                log.LogInformation("Received frame: {Length} bytes", frame.Data.Length);
                receivedFrames.Add(frame.Data);
            }
            log.LogInformation("Finished collecting frames, total: {Count}", receivedFrames.Count);
        }
        catch (OperationCanceledException) {
            log.LogInformation("Frame collection cancelled, got {Count} frames", receivedFrames.Count);
        }
        catch (Exception ex) {
            log.LogError(ex, "Error collecting frames");
        }
        finally {
            frameCollectionTcs.TrySetResult();
        }
    }

    // Private methods

    private static async IAsyncEnumerable<AudioFrame> GetTestAudioFrames()
    {
        // Generate some test audio frames
        var offset = TimeSpan.Zero;
        var duration = Constants.Audio.OpusFrameDuration;

        for (var i = 0; i < 50; i++) {
            // Generate a simple test frame (not valid opus, but good for testing the pipeline)
            var data = new byte[100];
            Array.Fill(data, (byte)(i % 256));

            yield return new AudioFrame {
                Data = data,
                Offset = offset,
                Duration = duration,
            };

            offset += duration;
            await Task.Delay(10); // Simulate real-time recording
        }
    }
}
