using System.Security;
using ActualChat.Audio;
using ActualChat.Live;
using ActualChat.Testing.Host;
using ActualLab.Rpc;

namespace ActualChat.Streaming.IntegrationTests;

[Collection(nameof(StreamingCollection))]
public class LiveAudioStreamsTest(AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    [Fact]
    public void OnlyCurrentStreamingApisAreRegistered()
    {
        // act
        var serviceNames = AppHost.Services.RpcHub().ServiceRegistry
            .Select(x => x.Name)
            .ToList();

        // assert
        serviceNames.Should().Contain(nameof(ILiveAudioStreams));
        serviceNames.Should().Contain(nameof(ILiveVideoStreams));
        serviceNames.Should().NotContain("IStreamServer");
    }

    [Fact(Timeout = 60_000)]
    public async Task NonMemberCannotReadAudioStream()
    {
        // arrange
        var appHost = AppHost;
        var services = appHost.Services;
        var commander = services.Commander();
        var memberSession = Session.New();
        _ = await appHost.SignIn(memberSession, new AccountFull("Bobby"));
        var nonMemberSession = Session.New();
        _ = await appHost.SignIn(nonMemberSession, new AccountFull("Jack"));

        var chat = await commander.Call(new Chats_Change(memberSession, default, null, new() {
            Create = new ChatDiff {
                Title = "NonMemberAudioTest",
                Kind = ChatKind.Group,
            },
        }));
        chat.Require();

        // JustVoice keeps this recording out of the transcription pipeline.
        await services.UserSettingsUI(memberSession)
            .ChatUserSettings(chat.Id)
            .Set(new ChatUserSettings { VoiceMode = VoiceMode.JustVoice }, CancellationToken.None);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        var backend = services.GetRequiredService<IAudioStreamingBackend>();
        var liveStreams = services.GetRequiredService<ILiveAudioStreams>();
        var record = new AudioRecord(
            StreamId.New(services.MeshWatcher().ThisNode.Ref),
            memberSession,
            chat.Id,
            SystemClock.Instance.Now.EpochOffset.TotalSeconds,
            null);
        var streamId = OpenAudioSegment.GetStreamId(record, 0).Value;
        var processTask = BackgroundTask.Run(
            () => backend.ProcessAudio(record, 0, new RpcStream<AudioFrame>(GetFrames()), cts.Token),
            cts.Token);

        // act
        var memberStream = await WaitForStream(liveStreams, memberSession, streamId, cts.Token);

        // assert
        memberStream.Should().NotBeNull();
        await Assert.ThrowsAsync<SecurityException>(
            () => liveStreams.GetStream(nonMemberSession, streamId, default, cts.Token));
        await Assert.ThrowsAsync<SecurityException>(
            () => liveStreams.GetTranscriptStream(nonMemberSession, streamId, cts.Token));
        var memberTranscript = await liveStreams.GetTranscriptStream(memberSession, streamId, cts.Token);
        memberTranscript.Should().BeNull();

        await cts.CancelAsync();
        await processTask.SilentAwait(false);
    }

    [Fact]
    public async Task GetStream_ShouldReturnStream()
    {
        var appHost = AppHost;
        var services = appHost.Services;
        var commander = services.Commander();
        var session = Session.New();
        _ = await appHost.SignIn(session, new AccountFull("Bobby"));

        var chat = await commander.Call(new Chats_Change(session, default, null, new() {
            Create = new ChatDiff {
                Title = "LiveStreamsTest",
                Kind = ChatKind.Group,
            },
        }));
        chat.Require();

        var liveStreams = services.GetRequiredService<ILiveAudioStreams>();

        var stream = await liveStreams.GetListeningStream(session, chat.Id, default, CancellationToken.None);

        stream.Should().NotBeNull();
    }

    [Fact]
    public async Task GetStream_ShouldReceiveItems()
    {
        var appHost = AppHost;
        var services = appHost.Services;
        var commander = services.Commander();
        var session = Session.New();
        _ = await appHost.SignIn(session, new AccountFull("Bobby"));

        // Create a chat
        var chat = await commander.Call(new Chats_Change(session, default, null, new() {
            Create = new ChatDiff {
                Title = "LiveStreamsTest",
                Kind = ChatKind.Group,
            },
        }));
        chat.Require();

        var liveStreams = services.GetRequiredService<ILiveAudioStreams>();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var stream = await liveStreams.GetListeningStream(session, chat.Id, default, cts.Token);

        // Stream should not throw when enumerated (even if empty)
        var items = new List<MuxedAudioStreamItem>();
        try {
            await foreach (var item in stream)
                items.Add(item);
        }
        catch (OperationCanceledException) {
            // Expected - no active streams
        }

        // No items expected since we didn't start any audio recording
        items.Should().BeEmpty();
    }

    [Fact]
    public async Task UpdateConfig_ShouldNotThrow()
    {
        var appHost = AppHost;
        var services = appHost.Services;
        var commander = services.Commander();
        var session = Session.New();
        _ = await appHost.SignIn(session, new AccountFull("Bobby"));

        var chat = await commander.Call(new Chats_Change(session, default, null, new() {
            Create = new ChatDiff {
                Title = "LiveStreamsTest",
                Kind = ChatKind.Group,
            },
        }));
        chat.Require();

        var liveStreams = services.GetRequiredService<ILiveAudioStreams>();
        var settings = new LegacyLiveStreamSettings { StreamKindFilter = LegacyLiveStreamKind.None };

        await liveStreams.LegacyChangeSettings(session, chat.Id, settings, CancellationToken.None);
        // Should not throw
    }

    // Private methods

    private static async Task<RpcStream<AudioFrame>?> WaitForStream(
        ILiveAudioStreams liveStreams,
        Session session,
        string streamId,
        CancellationToken cancellationToken)
    {
        while (true) {
            var stream = await liveStreams.GetStream(session, streamId, default, cancellationToken);
            if (stream != null)
                return stream;

            await Task.Delay(50, cancellationToken);
        }
    }

    private static async IAsyncEnumerable<AudioFrame> GetFrames()
    {
        var offset = TimeSpan.Zero;
        for (var i = 0; i < 25; i++) {
            var data = new byte[100];
            Array.Fill(data, (byte)i);
            yield return new AudioFrame {
                Data = data,
                Offset = offset,
                Duration = Constants.Audio.OpusFrameDuration,
            };

            offset += Constants.Audio.OpusFrameDuration;
            await Task.Delay(20);
        }
    }
}
