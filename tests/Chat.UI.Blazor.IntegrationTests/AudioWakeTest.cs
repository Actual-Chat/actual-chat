using ActualChat.Hosting;
using ActualChat.Testing.Host;
using ActualChat.UI.Blazor.App;
using ActualChat.UI.Blazor.App.Services;

namespace ActualChat.Chat.UI.Blazor.IntegrationTests;

[Collection(nameof(ChatUICollection))]
public sealed class AudioWakeTest(ChatAppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<ChatAppHostFixture>(fixture, @out)
{
    [Theory]
    [InlineData(AppKind.Android)]
    [InlineData(AppKind.Ios)]
    [InlineData(AppKind.Windows)]
    [InlineData(AppKind.MacOS)]
    public async Task NativeRecordingShouldSurviveWebViewWake(AppKind appKind)
    {
        // arrange
        using var appHost = await NewAppHost("chat-ui", o => o with {
            MustInitializeDb = false,
            ConfigureServices = (ctx, services) =>
                services.AddSingleton(ctx.HostInfo with { AppKind = appKind }),
        });
        await using var tester = appHost.NewBlazorTester(Out);
        await tester.SignInAsUniqueBob();
        var (chatId, _) = await tester.CreateChat(true);
        var hub = tester.ScopedAppServices.AppUIHub();
        await hub.ChatAudioUI.SetRecordingChatId(chatId, isPtt: true);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var resetTask = hub.ChatAudioUI.StopRecordingAndReplayOnDeviceAwake(cts.Token);

        // act
        hub.DeviceAwakeUI.OnDeviceAwake(30_000);
        await resetTask;

        // assert
        (await hub.ChatAudioUI.GetRecordingChatId()).Should().Be(chatId);
    }

    [Theory]
    [InlineData(AppKind.Android, true)]
    [InlineData(AppKind.Wasm, false)]
    public async Task WakeShouldPreserveNativeReplayButStopBrowserReplay(AppKind appKind, bool mustKeepReplay)
    {
        // arrange
        using var appHost = await NewAppHost("chat-ui", o => o with {
            MustInitializeDb = false,
            ConfigureServices = (ctx, services) =>
                services.AddSingleton(ctx.HostInfo with { AppKind = appKind }),
        });
        await using var tester = appHost.NewBlazorTester(Out);
        await tester.SignInAsUniqueBob();
        var (chatId, _) = await tester.CreateChat(true);
        var hub = tester.ScopedAppServices.AppUIHub();
        var replay = new ReplayState(chatId, hub.Clocks.ServerClock.Now);
        ((MutableState<ReplayState?>)hub.ChatAudioUI.ReplayState).Set(replay);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var resetTask = hub.ChatAudioUI.StopRecordingAndReplayOnDeviceAwake(cts.Token);

        // act
        hub.DeviceAwakeUI.OnDeviceAwake(30_000);
        await resetTask;

        // assert
        hub.ChatAudioUI.ReplayState.Value.Should().Be(mustKeepReplay ? replay : null);
    }

    [Fact]
    public async Task BrowserRecordingShouldStillStopOnWake()
    {
        // arrange
        await using var tester = AppHost.NewBlazorTester(Out);
        await tester.SignInAsUniqueBob();
        var (chatId, _) = await tester.CreateChat(true);
        var hub = tester.ScopedAppServices.AppUIHub();
        await hub.ChatAudioUI.SetRecordingChatId(chatId);
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var resetTask = hub.ChatAudioUI.StopRecordingAndReplayOnDeviceAwake(cts.Token);

        // act
        hub.DeviceAwakeUI.OnDeviceAwake(30_000);
        await resetTask;

        // assert
        (await hub.ChatAudioUI.GetRecordingChatId()).Should().BeNull();
    }
}
