using ActualChat.Hosting;
using ActualChat.Kvas;
using ActualChat.Localization;
using ActualChat.Testing.Host;
using ActualChat.UI.Blazor.App;
using ActualChat.UI.Blazor.App.Components;
using ActualChat.UI.Blazor.App.Services;
using ActualChat.UI.Blazor.Services;
using Bunit;
using Bunit.TestDoubles;
using Microsoft.AspNetCore.Components;

namespace ActualChat.Chat.UI.Blazor.IntegrationTests;

[Collection(nameof(ChatUICollection))]
public sealed class DesktopPttTest(ChatAppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<ChatAppHostFixture>(fixture, @out)
{
    [Theory]
    [InlineData(AppKind.Windows, false)]
    [InlineData(AppKind.Windows, true)]
    [InlineData(AppKind.MacOS, false)]
    [InlineData(AppKind.MacOS, true)]
    public async Task DesktopPttShouldListenWithoutOpeningAChatAndExposeItsSettings(
        AppKind appKind, bool isEnabledAtStartup)
    {
        // arrange
        BlazorTester? blazorTester = null;
        using var appHost = await NewAppHost("chat-ui", o => o with {
            MustInitializeDb = false,
            ConfigureServices = (ctx, services) => {
                services.AddSingleton(ctx.HostInfo with { AppKind = appKind });
                services.AddScoped<NavigationManager>(_ => new BunitNavigationManager(blazorTester!));
            },
        });
        await using var tester = appHost.NewBlazorTester(Out);
        blazorTester = tester;
        await tester.SignInAsUniqueBob();
        var hub = tester.ScopedAppServices.AppUIHub();
        tester.RenderModalHost(hub);
        var (chatId, _) = await tester.CreateChat(true);
        var enableCmd = new ChatsBackend_Change(
            chatId, null, Change.Update(new ChatDiff { PttEnabledAt = (Moment?)Moment.EpochStart }));
        var chat = await tester.AppServices.Commander().Call(enableCmd);
        await hub.UserSettingsUI.UserPttSettings().Update(x =>
            x.WithPttChat(chatId, Moment.Max(chat.PttEnabledAt!.Value, hub.Clocks.ServerClock.Now)));
        if (isEnabledAtStartup)
            await hub.LocalSettings.Set(Ptt.IsEnabledOnDeviceKey, Box.New(true));

        var audio = hub.ChatAudioUI;
        if (!isEnabledAtStartup) {
            audio.WhenEnabled.IsCompleted.Should().BeFalse();
            audio.SetIsPttEnabledOnDevice(true);
        }

        // act
        var settings = tester.Render<PttSettings>();

        // assert
        await TestWait.WhenPolled(() => audio.WhenEnabled.IsCompleted.Should().BeTrue());
        await TestWait.When(async ct =>
            (await audio.GetListeningPlayer(chatId, ct)).Should().NotBeNull());
        hub.ChatUI.SelectedChatId.Value.Should().NotBe(chatId);
        await TestWait.WhenRendered(settings, () => {
            settings.Markup.Should().Contain(hub.StringLocalizer.Ptt_UseOnDesktopCaption);
            settings.Markup.Should().NotContain(hub.StringLocalizer.Ptt_GesturesTopic);
            settings.Markup.Should().NotContain(hub.StringLocalizer.Ptt_LockScreenTopic);
        });

        // act
        hub.BrowserInfo.OnIsVisibleChanged(false);

        // assert
        await TestWait.When(async ct =>
            (await audio.GetListeningChatIds()).Should().Contain(chatId));

        // act
        audio.SetIsPttEnabledOnDevice(false);

        // assert
        await TestWait.When(async ct =>
            (await audio.GetListeningChatIds()).Should().NotContain(chatId));
        (await audio.GetConsentedPttChatIds(CancellationToken.None)).Should().Contain(chatId);

        // act
        audio.SetIsPttEnabledOnDevice(true);

        // assert
        await TestWait.When(async ct =>
            (await audio.GetListeningPlayer(chatId, ct)).Should().NotBeNull());
    }
}
