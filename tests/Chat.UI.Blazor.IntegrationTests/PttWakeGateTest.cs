using ActualChat.Testing.Host;
using ActualChat.UI.Blazor.App;
using ActualChat.UI.Blazor.App.Services;

namespace ActualChat.Chat.UI.Blazor.IntegrationTests;

// The gates PttSessionCore.StartPlayback applies before a wake plays anything: the per-device
// PTT switch, and the phone's own silent/vibrate/DND state.

[Collection(nameof(ChatUICollection))]
public sealed class PttWakeGateTest(ChatAppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<ChatAppHostFixture>(fixture, @out)
{
    // Both gates short-circuit before any chat work, so an id that resolves to nothing is enough.
    private static readonly ChatId UnvisitedChatId = ChatId.Parse("testchatid1234567890");

    [Fact]
    public async Task AWakeShouldBeIgnoredWhilePttIsOffOnThisDevice()
    {
        // arrange
        await using var tester = AppHost.NewBlazorTester(Out);
        await tester.SignInAsUniqueBob();
        tester.ScopedAppServices.AppUIHub().ChatAudioUI.SetIsPttEnabledOnDevice(false);

        // act
        var reason = await StartPlayback(tester, new TestPttPlatform(), UnvisitedChatId);

        // assert
        reason.Should().Be(PttWakeIgnoreReason.DeviceDisabled);
    }

    [Fact]
    public async Task AWakeShouldBeIgnoredWhileThePhoneIsSilenced()
    {
        // arrange
        await using var tester = AppHost.NewBlazorTester(Out);
        await tester.SignInAsUniqueBob();
        tester.ScopedAppServices.AppUIHub().ChatAudioUI.SetIsPttEnabledOnDevice(true);

        // act: the device switch is on, so only the silence gate can stop this wake
        var reason = await StartPlayback(tester, new TestPttPlatform(isSilenced: true), UnvisitedChatId);

        // assert
        reason.Should().Be(PttWakeIgnoreReason.Silenced);
    }

    [Fact]
    public async Task AForegroundWakeShouldSurviveASilencedPhone()
    {
        // arrange
        await using var tester = AppHost.NewBlazorTester(Out);
        await tester.SignInAsUniqueBob();
        var (chatId, _) = await tester.CreateChat(true);
        tester.ScopedAppServices.AppUIHub().ChatAudioUI.SetIsPttEnabledOnDevice(true);

        // act: silence governs alerts, not playback the user is already looking at
        var reason = await StartPlayback(
            tester, new TestPttPlatform(isSilenced: true), chatId, isForeground: true);

        // assert
        reason.Should().BeNull("a foreground wake is playback the user is looking at, not an alert");
    }

    // Private methods

    private static Task<PttWakeIgnoreReason?> StartPlayback(
        BlazorTester tester,
        TestPttPlatform platform,
        ChatId chatId,
        bool isForeground = false)
        => tester.ScopedAppServices
            .GetRequiredService<PttSessionCore>()
            .StartPlayback(chatId, Moment.EpochStart, isForeground, isHeadless: false, platform);

    // Nested types

    private sealed class TestPttPlatform(bool isSilenced = false) : PttPlatform
    {
        public override bool IsSilenced { get; } = isSilenced;

        public override void OnWakeFailed(ChatId chatId)
        { }

        public override void OnHeadlessTeardown()
        { }
    }
}
