using ActualChat.Chat;
using ActualChat.Live;
using ActualChat.Testing.Host;

namespace ActualChat.Streaming.IntegrationTests;

[Collection(nameof(StreamingCollection))]
public class LiveAudioStreamsTest(AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
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

        var stream = await liveStreams.GetListeningStream(session, chat.Id, CancellationToken.None);

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
        var stream = await liveStreams.GetListeningStream(session, chat.Id, cts.Token);

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
}
