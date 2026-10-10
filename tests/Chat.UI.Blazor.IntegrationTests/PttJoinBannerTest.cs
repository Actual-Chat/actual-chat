using ActualChat.Localization;
using ActualChat.Testing.Host;
using ActualChat.UI.Blazor.App;
using ActualChat.UI.Blazor.App.Components;
using ActualChat.UI.Blazor.Components;
using Bunit;

namespace ActualChat.Chat.UI.Blazor.IntegrationTests;

[Collection(nameof(ChatUICollection))]
public sealed class PttJoinBannerTest(ChatAppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<ChatAppHostFixture>(fixture, @out)
{
    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public async Task BannerShouldAskOnlyForMissingChatOrDeviceConsent(bool isConsented, bool isDeviceEnabled)
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
        if (isConsented)
            await hub.ChatAudioUI.ConsentPtt(chatId, chat.PttEnabledAt!.Value, CancellationToken.None);
        hub.ChatAudioUI.SetIsPttEnabledOnDevice(isDeviceEnabled);
        await TestWait.When(async ct =>
            (await hub.ChatAudioUI.IsPttEnabledOnDevice(ct)).Should().Be(isDeviceEnabled));
        tester.RenderModalHost(hub);

        // act
        var banner = tester.Render<PttJoinBanner>(p => p.AddCascadingValue(new ChatContext(hub, chat)));

        // assert
        await TestWait.WhenRendered(banner, () =>
            banner.FindComponent<Banner>().Instance.IsVisible.Should().Be(!isConsented || !isDeviceEnabled));
        if (isConsented && isDeviceEnabled)
            return;

        var l = hub.StringLocalizer;
        var text = isConsented ? l.Banner_PttUseDevice
            : isDeviceEnabled ? l.Banner_PttAllow : l.Banner_PttAllowOnDevice;
        await TestWait.WhenRendered(banner, () => {
            banner.Find(".banner-body").TextContent.Trim().Should().Be(text);
            banner.FindComponent<BannerButton>().Find("button").TextContent.Trim()
                .Should().Be(isConsented ? l.Banner_PttTurnOn : l.Common_Allow);
        });

        // act
        await banner.InvokeAsync(() => banner.FindComponent<BannerButton>().Find("button").Click());

        // assert
        await TestWait.WhenRendered(banner, () => banner.FindComponent<Banner>().Instance.IsVisible.Should().BeFalse());
        (await hub.ChatAudioUI.IsPttEnabledOnDevice(CancellationToken.None)).Should().BeTrue();
        (await hub.ChatAudioUI.GetConsentedPttChatIds(CancellationToken.None)).Should().Contain(chatId);
    }

    [Fact]
    public async Task EnablingTheDeviceShouldRemoveDeviceWordingFromAnUnconsentedChatBanner()
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
        hub.ChatAudioUI.SetIsPttEnabledOnDevice(false);
        tester.RenderModalHost(hub);
        var banner = tester.Render<PttJoinBanner>(p => p.AddCascadingValue(new ChatContext(hub, chat)));
        await TestWait.WhenRendered(banner, () => banner.Find(".banner-body").TextContent.Trim()
            .Should().Be(hub.StringLocalizer.Banner_PttAllowOnDevice));

        // act
        hub.ChatAudioUI.SetIsPttEnabledOnDevice(true);

        // assert
        await TestWait.WhenRendered(banner, () => banner.Find(".banner-body").TextContent.Trim()
            .Should().Be(hub.StringLocalizer.Banner_PttAllow));
        (await hub.ChatAudioUI.GetConsentedPttChatIds(CancellationToken.None)).Should().NotContain(chatId);
    }
}
