using ActualChat.Localization;
using ActualChat.Testing.Host;
using ActualChat.UI.Blazor.App;
using ActualChat.UI.Blazor.App.Components;
using ActualChat.UI.Blazor.App.Services;
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
    public async Task BannerShouldAskOnlyForAnUnconsentedChatOnAnEnabledDevice(bool isConsented, bool isDeviceEnabled)
    {
        // arrange
        await using var tester = AppHost.NewBlazorTester(Out);
        await tester.SignInAsUniqueBob();
        var hub = tester.ScopedAppServices.AppUIHub();
        var chat = await CreatePttChat(tester, hub);
        if (isConsented)
            await hub.ChatAudioUI.ConsentPtt(chat.Id, chat.PttEnabledAt!.Value, CancellationToken.None);
        hub.ChatAudioUI.SetIsPttEnabledOnDevice(isDeviceEnabled);
        await TestWait.When(async ct =>
            (await hub.ChatAudioUI.IsPttEnabledOnDevice(ct)).Should().Be(isDeviceEnabled));
        tester.RenderModalHost(hub);

        // act
        var banner = tester.Render<PttJoinBanner>(p => p.AddCascadingValue(new ChatContext(hub, chat)));

        // assert
        var isExpectedVisible = !isConsented && isDeviceEnabled;
        await TestWait.WhenRendered(banner, () =>
            banner.FindComponent<Banner>().Instance.IsVisible.Should().Be(isExpectedVisible));
        if (!isExpectedVisible)
            return;

        var l = hub.StringLocalizer;
        await TestWait.WhenRendered(banner, () => {
            banner.Find(".banner-body").TextContent.Trim().Should().Be(l.Banner_PttAllow);
            banner.FindComponent<BannerButton>().Find("button").TextContent.Trim().Should().Be(l.Common_Allow);
        });

        // act
        await banner.InvokeAsync(() => banner.FindComponent<BannerButton>().Find("button").Click());

        // assert
        await TestWait.WhenRendered(banner, () => banner.FindComponent<Banner>().Instance.IsVisible.Should().BeFalse());
        (await hub.ChatAudioUI.GetConsentedPttChatIds(CancellationToken.None)).Should().Contain(chat.Id);
        // The tester is a web host, where PTT is only partially supported - so the Allow explains why
        var expectedLead = l.PttSupport_EnabledInChat_Format(chat.Title);
        await TestWait.When(async ct => (await hub.ModalUI.ActiveModals.Use(ct))
            .Select(x => x.Model)
            .OfType<PttSupportModal.Model>()
            .Should().ContainSingle(x => x.Lead == expectedLead));
    }

    [Fact]
    public async Task EnablingTheDeviceShouldShowTheBannerOfAnUnconsentedChat()
    {
        // arrange
        await using var tester = AppHost.NewBlazorTester(Out);
        await tester.SignInAsUniqueBob();
        var hub = tester.ScopedAppServices.AppUIHub();
        var chat = await CreatePttChat(tester, hub);
        hub.ChatAudioUI.SetIsPttEnabledOnDevice(false);
        tester.RenderModalHost(hub);
        var banner = tester.Render<PttJoinBanner>(p => p.AddCascadingValue(new ChatContext(hub, chat)));
        await TestWait.WhenRendered(banner, () =>
            banner.FindComponent<Banner>().Instance.IsVisible.Should().BeFalse());

        // act
        hub.ChatAudioUI.SetIsPttEnabledOnDevice(true);

        // assert
        await TestWait.WhenRendered(banner, () =>
            banner.FindComponent<Banner>().Instance.IsVisible.Should().BeTrue());
        (await hub.ChatAudioUI.GetConsentedPttChatIds(CancellationToken.None)).Should().NotContain(chat.Id);
    }

    // Private methods

    private static async Task<Chat> CreatePttChat(BlazorTester tester, AppUIHub hub)
    {
        var (chatId, _) = await tester.CreateChat(true);
        var command = new ChatsBackend_Change(
            chatId, null, Change.Update(new ChatDiff { PttEnabledAt = (Moment?)Moment.EpochStart }));
        await tester.AppServices.Commander().Call(command);
        return (await hub.Chats.Get(tester.Session, chatId, default))!;
    }
}
