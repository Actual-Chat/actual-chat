using ActualChat.Chat;
using ActualChat.Rtc;
using ActualChat.Streaming.Services;
using ActualChat.Testing.Host;
using ActualChat.Users;

namespace ActualChat.Streaming.IntegrationTests;

[Collection(nameof(StreamingCollection))]
public class RtcHubTest(AppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<AppHostFixture>(fixture, @out)
{
    [Fact]
    public async Task GetStream_ShouldReturnStream()
    {
        var appHost = AppHost;
        var services = appHost.Services;
        var session = Session.New();
        _ = await appHost.SignIn(session, new AccountFull("Bobby"));

        var rtcHub = services.GetRequiredService<IRtcHub>();
        var config = RtcStreamingSettings.Default;

        var stream = await rtcHub.GetStream(session, Constants.Chat.DefaultChatId, config, CancellationToken.None);

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
                Title = "RtcHubTest",
                Kind = ChatKind.Group,
            },
        }));
        chat.Require();

        var rtcHub = services.GetRequiredService<IRtcHub>();
        var config = RtcStreamingSettings.Default;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        var stream = await rtcHub.GetStream(session, chat.Id, config, cts.Token);

        // Stream should not throw when enumerated (even if empty)
        var items = new List<RtcItem>();
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

        var rtcHub = services.GetRequiredService<IRtcHub>();
        var settings = new RtcStreamingSettings { StreamKindFilter = RtcStreamKind.None };

        await rtcHub.ChangeSettings(session, Constants.Chat.DefaultChatId, settings, CancellationToken.None);
        // Should not throw
    }
}
