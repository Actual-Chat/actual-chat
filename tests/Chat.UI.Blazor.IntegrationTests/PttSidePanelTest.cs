using ActualChat.Localization;
using ActualChat.Testing.Host;
using ActualChat.UI.Blazor.App;
using ActualChat.UI.Blazor.App.Components;
using ActualChat.UI.Blazor.Components;
using Bunit;

namespace ActualChat.Chat.UI.Blazor.IntegrationTests;

[Collection(nameof(ChatUICollection))]
public sealed class PttSidePanelTest(ChatAppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<ChatAppHostFixture>(fixture, @out)
{
    [Theory]
    [InlineData(false, false, false, false, false,
        "VoiceSettings_PttNotEnabled")]
    [InlineData(true, true, true, true, false,
        "Ptt_ChatPaused")]
    [InlineData(true, false, false, true, false,
        "Ptt_AvailableNotAllowed")]
    [InlineData(true, false, true, false, false,
        "Ptt_AvailableDeviceOff")]
    [InlineData(true, false, true, true, true,
        "Ptt_AvailableMuted")]
    [InlineData(true, false, true, true, false,
        "Ptt_AvailableAllowed")]
    public async Task CaptionShouldDistinguishAvailabilityFromPersonalConsent(
        bool isEnabled, bool isPaused, bool isAllowed,
        bool isDeviceEnabled, bool isMuted, string expectedKey)
    {
        // arrange
        await using var tester = AppHost.NewBlazorTester(Out);
        await tester.SignInAsUniqueBob();
        var hub = tester.ScopedAppServices.AppUIHub();
        var (chatId, _) = await tester.CreateChat(true);
        if (isEnabled) {
            var command = new ChatsBackend_Change(chatId, null, Change.Update(new ChatDiff {
                PttEnabledAt = (Moment?)Moment.EpochStart,
                IsPttPaused = isPaused,
            }));
            await tester.AppServices.Commander().Call(command);
        }
        var chat = (await hub.Chats.Get(tester.Session, chatId, default))!;
        if (isAllowed)
            await hub.ChatAudioUI.ConsentPtt(chatId, chat.PttEnabledAt!.Value, CancellationToken.None);
        if (isMuted) {
            var now = hub.Clocks.ServerClock.Now;
            await hub.UserSettingsUI.UserPttSettings().Update(x =>
                x.WithPttChatMuted(chatId, now, now + TimeSpan.FromMinutes(15)));
        }
        hub.ChatAudioUI.SetIsPttEnabledOnDevice(isDeviceEnabled);
        tester.RenderModalHost(hub);

        // act
        var panel = tester.Render<ChatSidePanelInfo>(p => p.AddCascadingValue(new ChatContext(hub, chat)));

        // assert
        await TestWait.WhenRendered(panel, () => panel.FindComponents<TileItem>()
            .Single(x => x.Markup.Contains("icon-push-to-talk"))
            .Find(".ti-caption").TextContent.Trim().Should().Be(hub.StringLocalizer[expectedKey].Value));
    }

    [Fact]
    public async Task CaptionShouldReactToConsentDeviceAndMuteChanges()
    {
        // arrange
        await using var tester = AppHost.NewBlazorTester(Out);
        await tester.SignInAsUniqueBob();
        var hub = tester.ScopedAppServices.AppUIHub();
        var (chatId, _) = await tester.CreateChat(true);
        var command = new ChatsBackend_Change(
            chatId, null, Change.Update(new ChatDiff { PttEnabledAt = (Moment?)Moment.EpochStart }));
        await tester.AppServices.Commander().Call(command);
        var chat = (await hub.Chats.Get(tester.Session, chatId, default))!;
        hub.ChatAudioUI.SetIsPttEnabledOnDevice(true);
        tester.RenderModalHost(hub);
        var panel = tester.Render<ChatSidePanelInfo>(p => p.AddCascadingValue(new ChatContext(hub, chat)));
        var l = hub.StringLocalizer;
        await TestWait.WhenRendered(panel, () => panel.Markup.Should().Contain(l.Ptt_AvailableNotAllowed));

        // act
        await hub.ChatAudioUI.ConsentPtt(chatId, chat.PttEnabledAt!.Value, CancellationToken.None);

        // assert
        await TestWait.WhenRendered(panel, () => panel.Markup.Should().Contain(l.Ptt_AvailableAllowed));

        // act
        var now = hub.Clocks.ServerClock.Now;
        await hub.UserSettingsUI.UserPttSettings().Update(x =>
            x.WithPttChatMuted(chatId, now, now + TimeSpan.FromMinutes(15)));

        // assert
        await TestWait.WhenRendered(panel, () => panel.Markup.Should().Contain(l.Ptt_AvailableMuted));

        // act
        hub.ChatAudioUI.SetIsPttEnabledOnDevice(false);

        // assert
        await TestWait.WhenRendered(panel, () => panel.Markup.Should().Contain(l.Ptt_AvailableDeviceOff));

        // act
        hub.ChatAudioUI.SetIsPttEnabledOnDevice(true);
        await hub.ChatAudioUI.UnmutePtt([chatId], CancellationToken.None);

        // assert
        await TestWait.WhenRendered(panel, () => panel.Markup.Should().Contain(l.Ptt_AvailableAllowed));

        // act
        await hub.UserSettingsUI.UserPttSettings().Update(x => x.WithoutPttChat(chatId));

        // assert
        await TestWait.WhenRendered(panel, () => panel.Markup.Should().Contain(l.Ptt_AvailableNotAllowed));
    }
}
