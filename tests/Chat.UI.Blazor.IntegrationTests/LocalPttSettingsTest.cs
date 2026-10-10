using ActualChat.Hosting;
using ActualChat.Localization;
using ActualChat.Testing.Host;
using ActualChat.UI.Blazor.App;
using ActualChat.UI.Blazor.App.Components;
using ActualChat.UI.Blazor.App.Components.Settings;
using ActualChat.UI.Blazor.Components;
using Bunit;
using Bunit.TestDoubles;
using Microsoft.AspNetCore.Components;

namespace ActualChat.Chat.UI.Blazor.IntegrationTests;

[Collection(nameof(ChatUICollection))]
public sealed class LocalPttSettingsTest(ChatAppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<ChatAppHostFixture>(fixture, @out)
{
    [Theory]
    [InlineData("local.voxt.ai", true)]
    [InlineData("voxt.ai", false)]
    public async Task LocalWebShouldExposeAllPttSettingsWithoutAdminRights(string hostname, bool isPreview)
    {
        // arrange
        BlazorTester? blazorTester = null;
        using var appHost = await NewAppHost("chat-ui", o => o with {
            MustInitializeDb = false,
            ConfigureServices = (ctx, services) => {
                services.AddSingleton(new HostInfo {
                    BaseUrl = $"https://{hostname}/",
                    HostKind = ctx.HostInfo.HostKind,
                    AppKind = ctx.HostInfo.AppKind,
                    Environment = ctx.HostInfo.Environment,
                    Configuration = ctx.HostInfo.Configuration,
                    Roles = ctx.HostInfo.Roles,
                    DeviceModel = ctx.HostInfo.DeviceModel,
                    IsTested = ctx.HostInfo.IsTested,
                });
                services.AddScoped<NavigationManager>(_ => new BunitNavigationManager(blazorTester!));
            },
        });
        await using var tester = appHost.NewBlazorTester(Out);
        blazorTester = tester;
        var account = await tester.SignInAsUniqueBob();
        account.IsAdmin.Should().BeFalse();
        var hub = tester.ScopedAppServices.AppUIHub();
        var host = tester.RenderModalHost(hub);

        // act
        await hub.ModalUI.Show(new SettingsModal.Model(SettingsTabId.Ptt));

        // assert
        await TestWait.WhenRendered(host, () =>
            host.FindComponent<SettingsPanel>().Instance.Tabs
                .Any(x => x.Id == SettingsTabId.Ptt).Should().Be(isPreview));
        if (!isPreview)
            return;

        await TestWait.WhenRendered(host, () => {
            var settings = host.FindComponent<PttSettings>();
            var l = hub.StringLocalizer;
            settings.Markup.Should().Contain(l.Ptt_UseOnThisDevice);
            settings.Markup.Should().Contain(l.Ptt_FlipToTalk);
            settings.Markup.Should().Contain(l.Ptt_DoubleShake);
            settings.Markup.Should().Contain(l.Ptt_HushGesture);
            settings.Markup.Should().Contain(l.Ptt_HeadsetButton);
            settings.Markup.Should().Contain(l.Ptt_AlwaysListen);
            settings.Markup.Should().Contain(l.Ptt_NoMotionSensor);
            settings.Markup.Should().Contain(l.Ptt_LockScreenTalk);
            settings.FindComponents<PttHushDurationSettings>().Should().ContainSingle();
            settings.FindComponents<PttAnswerWindowSettings>().Should().ContainSingle();
            settings.FindComponents<PttReplyWindowSettings>().Should().ContainSingle();
            settings.FindComponents<PttPracticePanel>().Should().ContainSingle();
        });

        // act
        await host.InvokeAsync(() => host.FindComponent<PttSettings>()
            .FindComponents<TileItem>().Single(x => x.Markup.Contains(hub.StringLocalizer.Ptt_DoubleShake))
            .Find(".tile-item").Click());

        // assert
        await TestWait.WhenRendered(host, () => host.FindComponent<PttSettings>()
            .FindComponents<PttShakeSensitivitySettings>().Should().ContainSingle());
    }
}
