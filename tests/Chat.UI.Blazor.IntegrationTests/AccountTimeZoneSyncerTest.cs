using ActualChat.Testing.Host;
using ActualChat.UI.Blazor.App.Services;
using ActualChat.UI.Blazor.Services;
using ActualChat.Users;

namespace ActualChat.Chat.UI.Blazor.IntegrationTests;

[Collection(nameof(ChatUICollection))]
public class AccountTimeZoneSyncerTest(ChatAppHostFixture fixture, ITestOutputHelper @out)
    : SharedAppHostTestBase<ChatAppHostFixture>(fixture, @out)
{
    private BlazorTester Tester => field ??= AppHost.NewBlazorTester(Out);

    protected override async Task DisposeAsync()
    {
        await Tester.DisposeSilentlyAsync();
        await base.DisposeAsync();
    }

    [Theory]
    [InlineData("Europe/Berlin", "Europe/Berlin")]
    [InlineData("W. Europe Standard Time", "Europe/Berlin")]
    public async Task ShouldFillEmptyTimeZoneFromDevice(string deviceTimeZone, string expectedTimeZone)
    {
        // arrange
        var account = await Tester.SignInAsUniqueBob();
        account.TimeZone.Should().BeEmpty("a fresh account has no time zone");
        SetDeviceTimeZone(deviceTimeZone);

        // act
        Tester.ScopedAppServices.GetRequiredService<AccountTimeZoneSyncer>().Start();

        // assert
        await ComputedTest.When(async ct => {
            var own = await Tester.Accounts.GetOwn(Tester.Session, ct);
            own.TimeZone.Should().Be(expectedTimeZone);
        }, TimeSpan.FromSeconds(15));
    }

    [Fact]
    public async Task ShouldKeepTheTimeZoneTheUserChose()
    {
        // arrange
        var account = await Tester.SignInAsUniqueBob();
        await Tester.Commander.Call(new Accounts_Update {
            Session = Tester.Session,
            Account = account with { TimeZone = "America/New_York" },
            ExpectedVersion = account.Version,
        });
        SetDeviceTimeZone("Europe/Berlin");

        // act
        Tester.ScopedAppServices.GetRequiredService<AccountTimeZoneSyncer>().Start();
        await Task.Delay(TimeSpan.FromSeconds(2));

        // assert
        var own = await Tester.Accounts.GetOwn(Tester.Session, CancellationToken.None);
        own.TimeZone.Should().Be("America/New_York");
    }

    private void SetDeviceTimeZone(string timeZone)
    {
        var browserInfo = Tester.ScopedAppServices.GetRequiredService<BrowserInfo>();
        browserInfo.OnInitialized(new IBrowserInfoBackend.InitResult(
            ScreenSizeText: nameof(ScreenSize.Unknown),
            WindowHeight: 0,
            IsVisible: true,
            IsHoverable: false,
            ThemeInfo: new IBrowserInfoBackend.ThemeInfo(null, nameof(Theme.Light), nameof(Theme.Light), ""),
            UILanguageInfo: new IBrowserInfoBackend.UILanguageInfo(null, null, []),
            DefaultTheme: nameof(Theme.Light),
            UtcOffset: 0,
            TimeZone: timeZone,
            IsMobile: false,
            IsAndroid: false,
            IsIos: false,
            IsMacOS: false,
            IsChromium: false,
            IsEdge: false,
            IsWebKit: false,
            IsTouchCapable: false,
            CanVibrate: false,
            IsWasmReady: false,
            WindowId: ""));
    }
}
