using ActualChat.Chat;
using ActualChat.Live;
using ActualChat.Streaming.Services;
using ActualChat.Testing.Host;
using ActualChat.Users;

namespace ActualChat.Streaming.IntegrationTests;

[Collection(nameof(StreamingCollection))]
public class LiveHubTest(AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    [Fact]
    public async Task GetStream_ShouldReturnStream()
    {
        var appHost = AppHost;
        var services = appHost.Services;
        var session = Session.New();
        _ = await appHost.SignIn(session, new AccountFull("Bobby"));

        var liveHub = services.GetRequiredService<ILiveHub>();
        var config = LiveStreamingSettings.Default;

        var stream = await liveHub.GetStream(session, Constants.Chat.DefaultChatId, config, CancellationToken.None);

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
                Title = "LiveHubTest",
                Kind = ChatKind.Group,
            },
        }));
        chat.Require();

        var liveHub = services.GetRequiredService<ILiveHub>();
        var config = LiveStreamingSettings.Default;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var stream = await liveHub.GetStream(session, chat.Id, config, cts.Token);

        // Stream should not throw when enumerated (even if empty)
        var items = new List<LiveItem>();
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
        var session = Session.New();
        _ = await appHost.SignIn(session, new AccountFull("Bobby"));

        var liveHub = services.GetRequiredService<ILiveHub>();
        var settings = new LiveStreamingSettings { StreamKindFilter = LiveStreamKind.None };

        await liveHub.ChangeSettings(session, Constants.Chat.DefaultChatId, settings, CancellationToken.None);
        // Should not throw
    }
}
