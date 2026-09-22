using System.Security;
using ActualChat.Audio;
using ActualChat.Live;
using ActualChat.Testing.Host;
using ActualLab.Rpc;

namespace ActualChat.Streaming.IntegrationTests;

[Collection(nameof(StreamingCollection))]
public sealed class LiveAudioStreamsTest(AppHostFixture fixture, ITestOutputHelper @out)
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

    [Fact]
    public void BothReportAudioLatencyOverloadsAreRegistered()
    {
        // act
        var serviceDef = AppHost.Services.RpcHub().ServiceRegistry[typeof(ILiveAudioStreams)];
        var methodNames = serviceDef.Methods.Select(x => x.Name).ToList();

        // assert
        // The wire method name carries the parameter count, and inbound dispatch is a lookup by
        // that name - so ":3" present means already-published clients still resolve their call.
        methodNames.Should().Contain("ReportAudioLatency:3");
        methodNames.Should().Contain("ReportAudioLatency:4");
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

        var chat = await commander.Call(new Chats_Change {
            Session = memberSession,
            ChatId = default,
            ExpectedVersion = null,
            Change = new() {
                Create = new ChatDiff {
                    Title = "NonMemberAudioTest",
                    Kind = ChatKind.Group,
                },
            },
        });
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
    public async Task GetStreamReturnsAStream()
    {
        // arrange
        var appHost = AppHost;
        var services = appHost.Services;
        var commander = services.Commander();
        var session = Session.New();
        _ = await appHost.SignIn(session, new AccountFull("Bobby"));

        var chat = await commander.Call(new Chats_Change {
            Session = session,
            ChatId = default,
            ExpectedVersion = null,
            Change = new() {
                Create = new ChatDiff {
                    Title = "LiveStreamsTest",
                    Kind = ChatKind.Group,
                },
            },
        });
        chat.Require();

        var liveStreams = services.GetRequiredService<ILiveAudioStreams>();

        // act
        var stream = await liveStreams.GetListeningStream(session, chat.Id, default, CancellationToken.None);

        // assert
        stream.Should().NotBeNull();
    }

    [Fact]
    public async Task GetStreamReceivesItems()
    {
        // arrange
        var appHost = AppHost;
        var services = appHost.Services;
        var commander = services.Commander();
        var session = Session.New();
        _ = await appHost.SignIn(session, new AccountFull("Bobby"));

        // Create a chat
        var chat = await commander.Call(new Chats_Change {
            Session = session,
            ChatId = default,
            ExpectedVersion = null,
            Change = new() {
                Create = new ChatDiff {
                    Title = "LiveStreamsTest",
                    Kind = ChatKind.Group,
                },
            },
        });
        chat.Require();

        var liveStreams = services.GetRequiredService<ILiveAudioStreams>();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        // act
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

        // assert
        // No items expected since we didn't start any audio recording
        items.Should().BeEmpty();
    }

    [Fact]
    public async Task UpdateConfigDoesNotThrow()
    {
        // arrange
        var appHost = AppHost;
        var services = appHost.Services;
        var commander = services.Commander();
        var session = Session.New();
        _ = await appHost.SignIn(session, new AccountFull("Bobby"));

        var chat = await commander.Call(new Chats_Change {
            Session = session,
            ChatId = default,
            ExpectedVersion = null,
            Change = new() {
                Create = new ChatDiff {
                    Title = "LiveStreamsTest",
                    Kind = ChatKind.Group,
                },
            },
        });
        chat.Require();

        var liveStreams = services.GetRequiredService<ILiveAudioStreams>();
        var settings = new LegacyLiveStreamSettings { StreamKindFilter = LegacyLiveStreamKind.None };

        // act, assert (no throw)
        await liveStreams.LegacyChangeSettings(session, chat.Id, settings, CancellationToken.None);
    }

    [Fact(Timeout = 60_000)]
    public async Task GetListeningStreamShouldTrackListenerPresence()
    {
        // arrange
        var appHost = AppHost;
        var services = appHost.Services;
        var commander = services.Commander();
        var session = Session.New();
        await appHost.SignIn(session, new AccountFull("Bobby"));

        var chat = await commander.Call(new Chats_Change {
            Session = session,
            ChatId = default,
            ExpectedVersion = null,
            Change = new() {
                Create = new ChatDiff {
                    Title = "GetListeningStreamPresenceTest",
                    Kind = ChatKind.Group,
                },
            },
        });
        chat.Require();

        var liveAudioStreams = services.GetRequiredService<ILiveAudioStreams>();
        var liveSessionsBackend = services.GetRequiredService<ILiveSessionsBackend>();
        var authors = services.GetRequiredService<IAuthors>();
        var author = await authors.GetOwn(session, chat.Id, default);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        // act - open the listening stream and start consuming it
        var stream = await liveAudioStreams.GetListeningStream(session, chat.Id, default, cts.Token);
        var consumeTask = BackgroundTask.Run(async () => {
            await foreach (var _ in stream.WithCancellation(cts.Token)) { }
        }, cts.Token);

        // assert - presence is registered while the stream is open
        await TestWait.When(async ct =>
            (await liveSessionsBackend.ListParticipants(chat.Id, ct)).Should().Contain(author!.Id));

        // act - the caller stops consuming (cancellation unwinds the async iterator's finally block)
        await cts.CancelAsync();
        await consumeTask.SilentAwait(false);

        // assert - presence goes with it, not with the 90s ParticipantStaleness backstop
        await TestWait.When(async ct =>
            (await liveSessionsBackend.ListParticipants(chat.Id, ct)).Should().NotContain(author!.Id));
    }

    [Fact(Timeout = 60_000)]
    public async Task AmbientSessionShouldLatchAcrossSequentialUtterances()
    {
        // arrange - two distinct authors in the same group chat, both on JustVoice
        var appHost = AppHost;
        var services = appHost.Services;
        var commander = services.Commander();
        var session1 = Session.New();
        await appHost.SignIn(session1, new AccountFull("Alice"));
        var session2 = Session.New();
        await appHost.SignIn(session2, new AccountFull("Bob"));

        var chat = await commander.Call(new Chats_Change {
            Session = session1,
            ChatId = default,
            ExpectedVersion = null,
            Change = new() {
                Create = new ChatDiff {
                    Title = "AmbientLatchTest",
                    Kind = ChatKind.Group,
                    IsPublic = true, // so Bob can join and latch the session
                },
            },
        });
        chat.Require();
        await commander.Call(new Authors_Join { Session = session2, ChatId = chat.Id });

        await services.UserSettingsUI(session1)
            .ChatUserSettings(chat.Id)
            .Set(new ChatUserSettings { VoiceMode = VoiceMode.JustVoice }, CancellationToken.None);
        await services.UserSettingsUI(session2)
            .ChatUserSettings(chat.Id)
            .Set(new ChatUserSettings { VoiceMode = VoiceMode.JustVoice }, CancellationToken.None);

        var backend = services.GetRequiredService<IAudioStreamingBackend>();
        var liveSessionsBackend = services.GetRequiredService<ILiveSessionsBackend>();
        var authors = services.GetRequiredService<IAuthors>();
        var author1 = await authors.GetOwn(session1, chat.Id, default);
        var author2 = await authors.GetOwn(session2, chat.Id, default);

        // act - Alice's VAD-delimited utterance runs to completion (GetFrames ends after ~500ms)
        // BEFORE Bob's utterance ever starts - this is the shape of the real ambient conversation:
        // each speaker's speech is segmented into its own ProcessAudio call by client-side VAD.
        var record1 = new AudioRecord(
            StreamId.New(services.MeshWatcher().ThisNode.Ref),
            session1,
            chat.Id,
            SystemClock.Instance.Now.EpochOffset.TotalSeconds,
            null);
        await backend.ProcessAudio(record1, 0, new RpcStream<AudioFrame>(GetFrames()), CancellationToken.None);

        // act - only now does Bob's utterance start
        var record2 = new AudioRecord(
            StreamId.New(services.MeshWatcher().ThisNode.Ref),
            session2,
            chat.Id,
            SystemClock.Instance.Now.EpochOffset.TotalSeconds,
            null);
        await backend.ProcessAudio(record2, 0, new RpcStream<AudioFrame>(GetFrames()), CancellationToken.None);

        // assert - Alice's utterance ending must not have torn the session down before Bob's utterance
        // could join it: the session must still exist, with both authors present and latched.
        await TestWait.When(async ct => {
            var live = await liveSessionsBackend.GetState(chat.Id, ct);
            live.Should().NotBeNull("Alice's stream ending must not tear down the session before Bob joins it");
            live!.AuthorIds.Should().Contain(author1!.Id);
            live.AuthorIds.Should().Contain(author2!.Id);
            live.SessionStartedAt.Should().NotBeNull("two distinct authors must latch the session");
        });
        var liveSession = await liveSessionsBackend.Get(chat.Id, default);
        liveSession.Should().NotBeNull();
        liveSession!.StartedAt.Should().NotBe(default);
    }

    [Fact(Timeout = 60_000)]
    public async Task SkipToLiveSkipsWhatTheProducerAlreadyProduced()
    {
        // arrange
        var appHost = AppHost;
        var services = appHost.Services;
        var commander = services.Commander();
        var session = Session.New();
        _ = await appHost.SignIn(session, new AccountFull("Bobby"));
        var chat = await commander.Call(new Chats_Change {
            Session = session,
            ChatId = default,
            ExpectedVersion = null,
            Change = new() {
                Create = new ChatDiff {
                    Title = "SkipToLiveTest",
                    Kind = ChatKind.Group,
                },
            },
        });
        chat.Require();
        await services.UserSettingsUI(session)
            .ChatUserSettings(chat.Id)
            .Set(new ChatUserSettings { VoiceMode = VoiceMode.JustVoice }, CancellationToken.None);

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(45));
        var backend = services.GetRequiredService<IAudioStreamingBackend>();
        var liveStreams = services.GetRequiredService<ILiveAudioStreams>();
        var record = new AudioRecord(
            StreamId.New(services.MeshWatcher().ThisNode.Ref),
            session,
            chat.Id,
            SystemClock.Instance.Now.EpochOffset.TotalSeconds,
            null);
        var streamId = OpenAudioSegment.GetStreamId(record, 0).Value;
        var processTask = BackgroundTask.Run(
            () => backend.ProcessAudio(record, 0, new RpcStream<AudioFrame>(GetFrames()), cts.Token),
            cts.Token);
        _ = await WaitForStream(liveStreams, session, streamId, cts.Token);
        await Task.Delay(TimeSpan.FromMilliseconds(200), cts.Token);

        // act
        var stream = await liveStreams.GetStream(session, streamId, Constants.Audio.SkipToLive, cts.Token);
        var firstDataFrame = await stream!
            .FirstAsync(f => f.Offset >= TimeSpan.Zero, cts.Token);

        // assert
        firstDataFrame.Offset.Should().BeGreaterThan(TimeSpan.FromMilliseconds(100),
            "frames produced before the request must not be replayed");

        await cts.CancelAsync();
        await processTask.SilentAwait(false);
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
